using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ream.Core.Models;
using Ream.Persistence.Io;
using Ream.Persistence.Json;

namespace Ream.Persistence.Storage;

/// <summary>Reads and writes the hand-editable config.json, degrading gracefully when it is damaged.</summary>
public sealed class AppConfigStore
{
    private const int ReadAttempts = 3;

    private readonly string _path;
    private string? _lastWrittenText;

    public AppConfigStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    /// <summary>
    /// Startup load. A missing file is created with defaults; an unreadable one is set aside
    /// (never deleted) and replaced with defaults so the app can always start.
    /// </summary>
    /// <param name="defaultDocumentsRoot">Written into a newly created config so the location is visible and editable.</param>
    public AppConfig Load(string defaultDocumentsRoot)
    {
        RecoverInterruptedWrite();

        if (!File.Exists(_path))
            return WriteDefaults(defaultDocumentsRoot);

        if (TryLoad(out var config, out var error))
            return config;

        string aside = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Move(_path, aside);
        Debug.WriteLine($"Set aside unusable config ({error}): {aside}");
        return WriteDefaults(defaultDocumentsRoot);
    }

    /// <summary>
    /// Reads the file without touching it, for live reloads: a typo halfway through an edit must never
    /// move or overwrite what the user is typing. On failure <paramref name="error"/> says what is wrong.
    /// </summary>
    public bool TryLoad(out AppConfig config, out string? error)
    {
        config = new AppConfig();

        string? text = ReadText(out error);
        if (text is null) return false;

        AppConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AppConfig>(text, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            error = $"not valid JSON: {ex.Message}";
            return false;
        }

        if (parsed is null || parsed.Layout is null || parsed.Animations is null || parsed.Keybindings is null)
        {
            error = "a required section (layout, animations, keybindings) is missing or null";
            return false;
        }

        error = Validate(parsed);
        if (error is not null) return false;

        // Anything the user left out keeps its default binding.
        foreach (var (action, gesture) in AppConfig.DefaultKeybindings())
            parsed.Keybindings.TryAdd(action, gesture);

        config = parsed;
        return true;
    }

    /// <summary>
    /// Changes some keys in config.json and leaves everything else as the user wrote it. Refuses (returning
    /// false, with the reason) rather than rewrite a file it can't read exactly - including one with comments
    /// or trailing commas, which saving would silently strip.
    /// </summary>
    public bool Update(Action<JsonObject> change, out string? error)
    {
        JsonObject root;
        if (File.Exists(_path))
        {
            string? text = ReadText(out error);
            if (text is null) return false;

            try
            {
                root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow })
                    as JsonObject ?? throw new JsonException("the top level is not an object");
            }
            catch (JsonException ex)
            {
                error = $"not saved - config.json is not plain JSON ({ex.Message}); edit it by hand instead";
                return false;
            }
        }
        else
        {
            root = new JsonObject();
        }

        change(root);

        try
        {
            string json = root.ToJsonString(JsonDefaults.Options);
            AtomicFile.WriteAllText(_path, json);
            _lastWrittenText = json;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"couldn't write config.json: {ex.Message}";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// True when config.json still holds exactly what <see cref="Update"/> last wrote - that is, a file-change
    /// notification which is only our own save coming back. Anything else (the user edited it) clears the memory.
    /// </summary>
    public bool IsOurOwnLastWrite()
    {
        if (_lastWrittenText is null) return false;

        if (ReadText(out _) == _lastWrittenText) return true;

        _lastWrittenText = null;
        return false;
    }

    private string? ReadText(out string? error)
    {
        error = null;
        if (!File.Exists(_path))
        {
            error = "config.json was not found";
            return null;
        }

        // An editor or sync client can hold the file for a moment while saving.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(_path);
            }
            catch (IOException ex) when (attempt < ReadAttempts)
            {
                Thread.Sleep(50 * attempt);
                Debug.WriteLine($"Retrying config read: {ex.Message}");
            }
            catch (IOException ex)
            {
                error = $"couldn't read config.json: {ex.Message}";
                return null;
            }
        }
    }

    private static string? Validate(AppConfig config)
    {
        double gap = config.Layout.GapPx;
        if (!double.IsFinite(gap) || gap is < 0 or > 200)
            return "layout.gapPx must be between 0 and 200";

        if (config.CanvasOpacity is < 0 or > 100)
            return "canvasOpacity must be between 0 and 100";

        string? border = config.Layout.FocusBorderColor;
        if (!string.IsNullOrWhiteSpace(border) && !Rgba.TryParse(border, out _))
            return "layout.focusBorderColor must look like #RRGGBB or #AARRGGBB";

        var durations = new (string Name, int Value)[]
        {
            ("workspaceSwitchMs", config.Animations.WorkspaceSwitchMs),
            ("columnFocusMs", config.Animations.ColumnFocusMs),
            ("resizeMs", config.Animations.ResizeMs),
        };
        foreach (var (name, value) in durations)
            if (value is < 0 or > 5000) return $"animations.{name} must be between 0 and 5000";

        return null;
    }

    /// <summary>A crash between writing config.json.tmp and replacing config.json leaves the temp file behind.</summary>
    private void RecoverInterruptedWrite()
    {
        string temp = _path + ".tmp";
        if (!File.Exists(temp)) return;

        try
        {
            if (!File.Exists(_path) && IsValidJson(File.ReadAllText(temp)))
                File.Move(temp, _path);
            else
                File.Move(temp, $"{_path}.interrupted-{DateTime.UtcNow:yyyyMMddHHmmss}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Couldn't recover interrupted config write: {ex.Message}");
        }
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private AppConfig WriteDefaults(string defaultDocumentsRoot)
    {
        var config = new AppConfig { DocumentsRoot = defaultDocumentsRoot };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(config, JsonDefaults.Options));
        return config;
    }
}
