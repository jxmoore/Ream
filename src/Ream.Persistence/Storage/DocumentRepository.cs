using System.Diagnostics;
using System.Text.Json;
using Ream.Core.Abstractions;
using Ream.Core.Models;
using Ream.Persistence.Io;
using Ream.Persistence.Json;
using static Ream.Persistence.Storage.StorageConstants;

namespace Ream.Persistence.Storage;

/// <summary>
/// File-based store: one folder per workspace holding one file per note plus a layout file,
/// and a root metadata file describing the workspaces.
/// </summary>
public sealed class DocumentRepository : IDocumentRepository, IAssetStore
{
    private const string AssetsFolderName = "assets";

    private readonly string _root;
    private readonly object _gate = new();

    // What is currently on disk, so Save only touches what changed.
    private readonly Dictionary<Guid, string> _noteFolders = [];
    private readonly Dictionary<Guid, string> _noteBodies = [];
    private readonly Dictionary<Guid, string> _assetFolders = [];
    private readonly Dictionary<string, string> _layoutJson = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);
    private string? _metadataJson;

    public DocumentRepository(string root)
    {
        _root = root;
    }

    public string Root => _root;

    public DocumentSnapshot Load()
    {
        lock (_gate)
        {
            bool rootExisted = Directory.Exists(_root);
            Directory.CreateDirectory(_root);
            RecoverInterruptedWrites();

            var metadata = ReadJson<MetadataFile>(Path.Combine(_root, MetadataFileName));
            var entries = (metadata?.Workspaces ?? [])
                .Where(e => IsSafeName(e.FolderName) && Directory.Exists(FolderPath(e.FolderName!)))
                .OrderBy(e => e.Order)
                .ToList();

            // Workspace folders missing from (or all of) the metadata are adopted rather than lost.
            var listed = entries.Select(e => e.FolderName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.GetDirectories(_root, WorkspaceFolderPrefix + "*").OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string folder = Path.GetFileName(directory);
                if (listed.Add(folder))
                    entries.Add(new WorkspaceEntryFile { Id = Guid.NewGuid(), FolderName = folder, Order = int.MaxValue });
            }

            _noteFolders.Clear();
            _noteBodies.Clear();
            _assetFolders.Clear();
            _layoutJson.Clear();
            _metadataJson = null;

            var workspaces = entries.Select(LoadWorkspace).ToList();
            _knownFolders = workspaces.Select(w => w.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool firstRun = !rootExisted || (metadata is null && workspaces.Count == 0);
            return new DocumentSnapshot(workspaces, metadata?.CurrentWorkspaceId, firstRun);
        }
    }

    public void Save(DocumentSnapshot snapshot)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_root);

            foreach (var workspace in snapshot.Workspaces)
                Directory.CreateDirectory(FolderPath(workspace.FolderName));

            var desired = new Dictionary<Guid, (string Folder, NoteSnapshot Note)>();
            foreach (var workspace in snapshot.Workspaces)
                foreach (var note in workspace.Notes)
                    desired[note.Id] = (workspace.FolderName, note);

            // Notes first, then layouts, then metadata: a crash in between leaves data the loader can adopt.
            foreach (var (id, (folder, note)) in desired)
                SaveNote(id, folder, note);

            foreach (var id in _noteFolders.Keys.Where(id => !desired.ContainsKey(id)).ToList())
            {
                TrashNote(_noteFolders[id], NoteFileName(id));
                TrashAssets(id);
                _noteFolders.Remove(id);
                _noteBodies.Remove(id);
            }

            foreach (var workspace in snapshot.Workspaces)
                SaveLayout(workspace);

            SaveMetadata(snapshot);

            var current = snapshot.Workspaces.Select(w => w.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in _knownFolders.Where(f => !current.Contains(f)).ToList())
            {
                DeleteEmptyWorkspaceFolder(folder);
                _layoutJson.Remove(folder);
            }
            _knownFolders = current;
        }
    }

    /// <summary>
    /// A crash between writing "x.tmp" and replacing "x" leaves the temp file behind. If the real file is
    /// missing and the temp file is complete it is promoted; anything else is set aside in .recovered,
    /// never deleted, since it may hold the user's last edit.
    /// </summary>
    private void RecoverInterruptedWrites()
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        foreach (var temp in Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories).ToList())
        {
            string relative = Path.GetRelativePath(_root, temp);
            if (relative.StartsWith(TrashFolderName, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(RecoveredFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                string target = temp[..^".tmp".Length];
                if (!File.Exists(target) && LooksComplete(temp))
                {
                    File.Move(temp, target);
                    Debug.WriteLine($"Recovered interrupted write: {relative}");
                    continue;
                }

                string aside = Path.Combine(_root, RecoveredFolderName, stamp, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
                File.Move(temp, aside);
                Debug.WriteLine($"Set aside interrupted write: {relative}");
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Couldn't recover '{relative}': {ex.Message}");
            }
        }
    }

    /// <summary>Whether a temp file holds a whole file of its kind, judged by what the real file would be.</summary>
    private static bool LooksComplete(string temp)
    {
        string kind = Path.GetExtension(temp[..^".tmp".Length]).ToLowerInvariant();

        try
        {
            switch (kind)
            {
                case ".json":
                    using (JsonDocument.Parse(File.ReadAllText(temp))) return true;
                case NoteExtension:
                    string text = File.ReadAllText(temp);
                    return !NoteContent.IsReamNote(text) || NoteContent.TryParse(text, out _);
                case ".png":
                    var bytes = File.ReadAllBytes(temp);
                    return bytes.Length > 12 && bytes.AsSpan(bytes.Length - 8, 4).SequenceEqual("IEND"u8);
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }

    private WorkspaceSnapshot LoadWorkspace(WorkspaceEntryFile entry)
    {
        string folder = entry.FolderName!;
        string directory = FolderPath(folder);
        var layout = ReadJson<LayoutFile>(Path.Combine(directory, LayoutFileName));

        var notes = new List<NoteSnapshot>();
        var seen = new HashSet<Guid>();
        var listedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in layout?.Notes ?? [])
        {
            if (!IsSafeName(item.FileName)) continue;
            listedFiles.Add(item.FileName!);

            string path = Path.Combine(directory, item.FileName!);
            if (!File.Exists(path) || !seen.Add(item.NoteId)) continue;

            string body = File.ReadAllText(path);
            double width = WidthPresets.Clamp(item.WidthFraction ?? item.Width?.Fraction() ?? WidthPresets.Default);
            notes.Add(new NoteSnapshot(item.NoteId, item.Title ?? TitleOf(body), body, width, item.IsFullscreen));
            Remember(item.NoteId, folder, body);
        }

        // Note files the layout doesn't know about (lost layout, or a crash before it was written).
        var extras = Directory.GetFiles(directory, "*" + NoteExtension)
            .OrderBy(File.GetCreationTimeUtc)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase);
        foreach (var path in extras)
        {
            string fileName = Path.GetFileName(path);
            if (listedFiles.Contains(fileName)) continue;
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out var id)) continue;
            if (!seen.Add(id)) continue;

            string body = File.ReadAllText(path);
            notes.Add(new NoteSnapshot(id, TitleOf(body), body, WidthPresets.Default, false));
            Remember(id, folder, body);
        }

        return new WorkspaceSnapshot(entry.Id, entry.Name, folder, notes, layout?.FocusedNoteId);
    }

    private void Remember(Guid id, string folder, string body)
    {
        _noteFolders[id] = folder;
        _noteBodies[id] = body;
        if (Directory.Exists(AssetDir(folder, id))) _assetFolders[id] = folder;
    }

    private static string TitleOf(string body) => NoteContent.DeriveTitle(NoteContent.ToPlainText(body));

    private void SaveNote(Guid id, string folder, NoteSnapshot note)
    {
        string fileName = NoteFileName(id);

        if (_noteFolders.TryGetValue(id, out var oldFolder) && !oldFolder.Equals(folder, StringComparison.OrdinalIgnoreCase))
        {
            string from = Path.Combine(FolderPath(oldFolder), fileName);
            string to = Path.Combine(FolderPath(folder), fileName);
            if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
        }

        if (_assetFolders.TryGetValue(id, out var assetFolder) && !assetFolder.Equals(folder, StringComparison.OrdinalIgnoreCase))
        {
            MoveAssets(id, assetFolder, folder);
            _assetFolders[id] = folder;
        }

        string path = Path.Combine(FolderPath(folder), fileName);
        bool bodyChanged = !_noteBodies.TryGetValue(id, out var oldBody) || !string.Equals(oldBody, note.Body, StringComparison.Ordinal);
        if (bodyChanged || !File.Exists(path))
            AtomicFile.WriteAllText(path, note.Body);

        Remember(id, folder, note.Body);
    }

    private void TrashNote(string folder, string fileName)
    {
        string from = Path.Combine(FolderPath(folder), fileName);
        if (!File.Exists(from)) return;

        string trash = Path.Combine(_root, TrashFolderName, folder);
        Directory.CreateDirectory(trash);

        string to = Path.Combine(trash, fileName);
        if (File.Exists(to))
            to = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(fileName)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{NoteExtension}");
        File.Move(from, to);
    }

    public string SaveAsset(string workspaceFolder, Guid noteId, byte[] png)
    {
        if (!IsSafeName(workspaceFolder))
            throw new ArgumentException("Unsafe workspace folder name.", nameof(workspaceFolder));

        lock (_gate)
        {
            // The note may have moved workspaces since its images were last written.
            if (_assetFolders.TryGetValue(noteId, out var recorded) && !recorded.Equals(workspaceFolder, StringComparison.OrdinalIgnoreCase))
                MoveAssets(noteId, recorded, workspaceFolder);

            string name = Guid.NewGuid().ToString("N") + ".png";
            AtomicFile.WriteAllBytes(Path.Combine(AssetDir(workspaceFolder, noteId), name), png);
            _assetFolders[noteId] = workspaceFolder;
            return name;
        }
    }

    public string? GetAssetPath(string workspaceFolder, Guid noteId, string name)
    {
        if (!IsSafeName(workspaceFolder) || !IsSafeName(name)) return null;

        lock (_gate)
        {
            string path = Path.Combine(AssetDir(workspaceFolder, noteId), name);
            if (File.Exists(path)) return path;

            if (_assetFolders.TryGetValue(noteId, out var recorded) && IsSafeName(recorded))
            {
                path = Path.Combine(AssetDir(recorded, noteId), name);
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }

    private void MoveAssets(Guid id, string fromFolder, string toFolder)
    {
        string from = AssetDir(fromFolder, id);
        string to = AssetDir(toFolder, id);
        if (!Directory.Exists(from) || from.Equals(to, StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (!Directory.Exists(to))
        {
            Directory.Move(from, to);
        }
        else
        {
            // Both exist (an image was pasted after the note moved): merge rather than lose either.
            foreach (var file in Directory.GetFiles(from))
            {
                string destination = Path.Combine(to, Path.GetFileName(file));
                if (!File.Exists(destination)) File.Move(file, destination);
            }
            TryDeleteEmptyDirectory(from);
        }

        TryDeleteEmptyDirectory(Path.GetDirectoryName(from)!);
    }

    private void TrashAssets(Guid id)
    {
        if (!_assetFolders.Remove(id, out var folder)) return;

        string from = AssetDir(folder, id);
        if (!Directory.Exists(from)) return;

        string to = Path.Combine(_root, TrashFolderName, folder, AssetsFolderName, id.ToString("N"));
        if (Directory.Exists(to)) to += $"-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);

        TryDeleteEmptyDirectory(Path.GetDirectoryName(from)!);
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory, recursive: false);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Left directory in place: {ex.Message}");
        }
    }

    private string AssetDir(string workspaceFolder, Guid noteId) =>
        Path.Combine(FolderPath(workspaceFolder), AssetsFolderName, noteId.ToString("N"));

    private void SaveLayout(WorkspaceSnapshot workspace)
    {
        var file = new LayoutFile
        {
            Notes = workspace.Notes
                .Select(n => new NoteEntryFile
                {
                    NoteId = n.Id,
                    FileName = NoteFileName(n.Id),
                    Title = n.Title,
                    WidthFraction = Math.Round(WidthPresets.Clamp(n.WidthFraction), 4),
                    IsFullscreen = n.IsFullscreen,
                })
                .ToList(),
            FocusedNoteId = workspace.FocusedNoteId,
        };

        string json = JsonSerializer.Serialize(file, JsonDefaults.Options);
        if (_layoutJson.TryGetValue(workspace.FolderName, out var last) && last == json) return;

        AtomicFile.WriteAllText(Path.Combine(FolderPath(workspace.FolderName), LayoutFileName), json);
        _layoutJson[workspace.FolderName] = json;
    }

    private void SaveMetadata(DocumentSnapshot snapshot)
    {
        var file = new MetadataFile
        {
            Workspaces = snapshot.Workspaces
                .Select((w, i) => new WorkspaceEntryFile { Id = w.Id, Name = w.Name, FolderName = w.FolderName, Order = i })
                .ToList(),
            CurrentWorkspaceId = snapshot.CurrentWorkspaceId,
        };

        string json = JsonSerializer.Serialize(file, JsonDefaults.Options);
        if (json == _metadataJson) return;

        AtomicFile.WriteAllText(Path.Combine(_root, MetadataFileName), json);
        _metadataJson = json;
    }

    private void DeleteEmptyWorkspaceFolder(string folder)
    {
        string directory = FolderPath(folder);
        if (!Directory.Exists(directory)) return;

        try
        {
            TryDeleteEmptyDirectory(Path.Combine(directory, AssetsFolderName));
            string layout = Path.Combine(directory, LayoutFileName);
            if (File.Exists(layout)) File.Delete(layout);
            Directory.Delete(directory, recursive: false);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Left workspace folder '{folder}' in place: {ex.Message}");
        }
    }

    private T? ReadJson<T>(string path) where T : class, IVersionedFile
    {
        if (!File.Exists(path)) return null;

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Options);
        }
        catch (JsonException)
        {
            value = null;
        }

        if (value is null)
        {
            Quarantine(path);
            return null;
        }

        if (value.SchemaVersion > SchemaVersion)
            throw new InvalidDataException($"'{path}' was written by a newer version of Ream and can't be opened safely.");

        return value;
    }

    private static void Quarantine(string path)
    {
        string aside = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Move(path, aside);
        Debug.WriteLine($"Set aside unreadable file: {aside}");
    }

    private string FolderPath(string folder) => Path.Combine(_root, folder);

    private static string NoteFileName(Guid id) => id.ToString("N") + NoteExtension;

    private static bool IsSafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name != "." && name != ".."
        && Path.GetFileName(name) == name
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
