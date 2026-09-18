using System.Diagnostics;
using System.Text.Json;
using Ream.Core.Models;
using Ream.Persistence.Io;
using Ream.Persistence.Json;

namespace Ream.Persistence.Storage;

/// <summary>Reads and writes the hand-editable config.json, degrading gracefully when it is damaged.</summary>
public sealed class AppConfigStore
{
    private readonly string _path;

    public AppConfigStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    /// <param name="defaultDocumentsRoot">Written into a newly created config so the location is visible and editable.</param>
    public AppConfig Load(string defaultDocumentsRoot)
    {
        if (!File.Exists(_path))
            return WriteDefaults(defaultDocumentsRoot);

        AppConfig? config = null;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"config.json is not valid JSON: {ex.Message}");
        }

        if (config is null || config.Layout is null || config.Animations is null || config.Keybindings is null)
        {
            string aside = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(_path, aside);
            Debug.WriteLine($"Set aside unreadable config: {aside}");
            return WriteDefaults(defaultDocumentsRoot);
        }

        // Anything the user left out keeps its default binding.
        foreach (var (action, gesture) in AppConfig.DefaultKeybindings())
            config.Keybindings.TryAdd(action, gesture);

        return config;
    }

    private AppConfig WriteDefaults(string defaultDocumentsRoot)
    {
        var config = new AppConfig { DocumentsRoot = defaultDocumentsRoot };
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(config, JsonDefaults.Options));
        return config;
    }
}
