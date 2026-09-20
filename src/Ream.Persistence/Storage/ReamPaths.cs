namespace Ream.Persistence.Storage;

/// <summary>
/// Where the pieces of a ream live. A ream is a <c>Foo.ream</c> file (JSON: the workspace list) plus a data folder holding
/// the workspace folders, notes and images. The ream's name is the file name; the .ream records its data folder
/// (<c>"dataFolder": "Foo"</c>, relative to the file, or <c>"."</c> for the file's own folder).
/// </summary>
public static class ReamPaths
{
    public const string Extension = ".ream";
    public const string LayoutExtension = ".reamlayout";
    public const string NoteExtension = ".reamnote";

    /// <summary>The data-folder value meaning "the same folder as the .ream file".</summary>
    public const string SameFolder = ".";

    private const int MaxNameLength = 100;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsReamFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The ream's name: its file name without the extension ("C:\x\Foo.ream" is "Foo").</summary>
    public static string NameOf(string reamFilePath) => Path.GetFileNameWithoutExtension(reamFilePath);

    /// <summary>The data folder a new ream gets: named after the file ("Foo").</summary>
    public static string DefaultDataFolder(string reamFilePath) => NameOf(reamFilePath);

    /// <summary>A data folder name read from a file or passed in: "." or one plain folder name, never a path.</summary>
    public static bool IsValidDataFolder(string? dataFolder) =>
        dataFolder == SameFolder || IsSafeSingleName(dataFolder);

    /// <summary>The folder that holds the workspace folders, notes and images for a ream file and its recorded data folder.</summary>
    public static string DataRootOf(string reamFilePath, string dataFolder)
    {
        if (!IsValidDataFolder(dataFolder)) throw new ArgumentException("Unsafe data folder name.", nameof(dataFolder));

        string directory = Path.GetDirectoryName(Path.GetFullPath(reamFilePath))!;
        return dataFolder == SameFolder ? directory : Path.Combine(directory, dataFolder);
    }

    /// <summary>Whether text can be a ream's name: something Windows accepts as a file name, not empty, not reserved, not absurdly long.</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name != name.Trim() || name.Length > MaxNameLength) return false;
        if (name.EndsWith('.')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

        // Windows reserves a device name with any extension too ("nul.txt", and so "nul.x.ream").
        string stem = name.Split('.')[0].TrimEnd(' ');
        return !ReservedNames.Contains(stem);
    }

    /// <summary>
    /// True if making a ream at this path would land on something that exists: the .ream file itself, or the data
    /// folder a new ream at that path would get. New and Save As refuse rather than merge into or replace it.
    /// </summary>
    public static bool IsOccupied(string reamFilePath)
    {
        string full = Path.GetFullPath(reamFilePath);
        return File.Exists(full) || Directory.Exists(DataRootOf(full, DefaultDataFolder(full)));
    }

    /// <summary>The first free path for a ream called <paramref name="baseName"/> in a folder: "Foo.ream", then "Foo 2.ream", "Foo 3.ream"...</summary>
    public static string UniquePath(string directory, string baseName)
    {
        for (int n = 1; ; n++)
        {
            string name = n == 1 ? baseName : $"{baseName} {n}";
            string path = Path.Combine(directory, name + Extension);
            if (!IsOccupied(path)) return path;
        }
    }

    private static bool IsSafeSingleName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name != "." && name != ".."
        && Path.GetFileName(name) == name
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
