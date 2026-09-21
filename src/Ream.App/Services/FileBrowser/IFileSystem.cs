namespace Ream.App.Services.FileBrowser;

internal sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified);

/// <summary>A shortcut in the browser's side list. <see cref="Group"/> is "Quick access" or "This PC".</summary>
internal sealed record BrowserPlace(string Name, string Path, string Group);

/// <summary>The file browser's only way to the disk, so tests can use an in-memory tree.</summary>
internal interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>The children of a folder, hidden and system items already left out. Throws UnauthorizedAccessException/IOException for a folder that cannot be read.</summary>
    IReadOnlyList<FileEntry> List(string directory);

    IReadOnlyList<BrowserPlace> Places();

    /// <summary>The folder above, or null at a drive root.</summary>
    string? ParentOf(string path);

    string Combine(string directory, string name);

    void CreateDirectory(string path);
}
