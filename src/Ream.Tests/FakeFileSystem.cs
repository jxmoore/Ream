using Ream.App.Services.FileBrowser;

namespace Ream.Tests;

/// <summary>An in-memory disk for the file browser: folders, files with dates, hidden items, folders that refuse to be read.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private static readonly DateTime DefaultDate = new(2026, 1, 1, 12, 0, 0);

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem()
    {
        AddDirectory(@"C:\");
    }

    /// <summary>What Places() returns. Set it to whatever the test needs.</summary>
    public List<BrowserPlace> PlaceList { get; } = [];

    /// <summary>Every call the model made, as "Method:argument".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>When set, CreateDirectory throws this instead of creating anything.</summary>
    public Exception? CreateDirectoryError { get; set; }

    public int ListCount => Calls.Count(c => c.StartsWith("List:", StringComparison.Ordinal));

    public FakeFileSystem AddDirectory(string path, DateTime? modified = null, bool hidden = false)
    {
        string key = Key(path);
        string? parent = ParentKey(key);
        if (parent is not null && !_nodes.ContainsKey(parent)) AddDirectory(parent);

        if (!_nodes.TryGetValue(key, out Node? node))
        {
            node = new Node(key, isDirectory: true);
            _nodes[key] = node;
            if (parent is not null) _nodes[parent].Children.Add(node);
        }

        node.Modified = modified ?? node.Modified;
        node.Hidden = hidden;
        return this;
    }

    public FakeFileSystem AddFile(string path, long size = 10, DateTime? modified = null, bool hidden = false)
    {
        string key = Key(path);
        string parent = ParentKey(key)!;
        if (!_nodes.ContainsKey(parent)) AddDirectory(parent);

        if (_nodes.TryGetValue(key, out Node? existing)) _nodes[parent].Children.Remove(existing);
        var node = new Node(key, isDirectory: false) { Size = size, Modified = modified ?? DefaultDate, Hidden = hidden };
        _nodes[key] = node;
        _nodes[parent].Children.Add(node);
        return this;
    }

    public FakeFileSystem Deny(string directory)
    {
        _denied.Add(Key(directory));
        return this;
    }

    public FakeFileSystem Allow(string directory)
    {
        _denied.Remove(Key(directory));
        return this;
    }

    public FakeFileSystem Remove(string path)
    {
        string key = Key(path);
        if (!_nodes.TryGetValue(key, out Node? node)) return this;

        foreach (Node child in node.Children.ToList()) Remove(child.Key);
        if (ParentKey(key) is { } parent && _nodes.TryGetValue(parent, out Node? parentNode)) parentNode.Children.Remove(node);
        _nodes.Remove(key);
        return this;
    }

    public FakeFileSystem AddPlace(string name, string path, string group)
    {
        PlaceList.Add(new BrowserPlace(name, path, group));
        return this;
    }

    public bool DirectoryExists(string path)
    {
        Calls.Add("DirectoryExists:" + path);
        return _nodes.TryGetValue(Key(path), out Node? node) && node.IsDirectory;
    }

    public bool FileExists(string path)
    {
        Calls.Add("FileExists:" + path);
        return _nodes.TryGetValue(Key(path), out Node? node) && !node.IsDirectory;
    }

    public IReadOnlyList<FileEntry> List(string directory)
    {
        Calls.Add("List:" + directory);
        string key = Key(directory);
        if (!_nodes.TryGetValue(key, out Node? node) || !node.IsDirectory) throw new DirectoryNotFoundException(directory);
        if (_denied.Contains(key)) throw new UnauthorizedAccessException($"Access to '{directory}' is denied.");

        return node.Children
            .Where(child => !child.Hidden)
            .Select(child => new FileEntry(child.Name, child.Key, child.IsDirectory, child.Size, child.Modified))
            .ToList();
    }

    public IReadOnlyList<BrowserPlace> Places()
    {
        Calls.Add("Places");
        return PlaceList.ToList();
    }

    public string? ParentOf(string path) => ParentKey(Key(path));

    public string Combine(string directory, string name) => Path.Combine(directory, name);

    public void CreateDirectory(string path)
    {
        Calls.Add("CreateDirectory:" + path);
        if (CreateDirectoryError is not null) throw CreateDirectoryError;
        AddDirectory(path, DateTime.Now);
    }

    private static string Key(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? ParentKey(string key) => Path.GetDirectoryName(key);

    private sealed class Node(string key, bool isDirectory)
    {
        public string Key { get; } = key;

        public string Name { get; } = Path.GetFileName(Path.TrimEndingDirectorySeparator(key)) is { Length: > 0 } name ? name : key;

        public bool IsDirectory { get; } = isDirectory;

        public long Size { get; set; }

        public DateTime Modified { get; set; } = DefaultDate;

        public bool Hidden { get; set; }

        public List<Node> Children { get; } = [];
    }
}
