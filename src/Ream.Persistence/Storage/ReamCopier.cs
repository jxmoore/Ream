using System.Text.Json;
using Ream.Persistence.Io;
using Ream.Persistence.Json;
using static Ream.Persistence.Storage.StorageConstants;

namespace Ream.Persistence.Storage;

/// <summary>Copies a whole ream to a new name: what Save As does.</summary>
public static class ReamCopier
{
    /// <summary>
    /// Copies the ream at <paramref name="sourceReamPath"/> to <paramref name="destReamPath"/>: a new <c>Bar.ream</c> and a
    /// data folder <c>Bar</c> beside it holding copies of the source's workspace folders (notes, layouts, images), and nothing
    /// else from the source's data folder (no trash, no recovered files, no temp files, no other reams' files).
    /// The .ream is written last, so a copy that fails part-way never leaves something openable, and whatever this call
    /// created is removed again. The source is never touched.
    /// </summary>
    /// <returns>The full path of the new .ream.</returns>
    /// <exception cref="FileNotFoundException">The source does not exist.</exception>
    /// <exception cref="InvalidDataException">The source is not a usable ream (not JSON, too new, unsafe data folder).</exception>
    /// <exception cref="IOException">The destination is taken, is the source, or sits inside the source; or a file could not be copied.</exception>
    public static string Copy(string sourceReamPath, string destReamPath, CancellationToken cancellation = default)
    {
        string source = Path.GetFullPath(sourceReamPath);
        if (!File.Exists(source)) throw new FileNotFoundException($"'{source}' does not exist.", source);
        if (!ReamPaths.IsReamFile(source)) throw new ArgumentException($"'{sourceReamPath}' is not a {ReamPaths.Extension} file.", nameof(sourceReamPath));
        if (!ReamPaths.IsReamFile(destReamPath)) throw new ArgumentException($"'{destReamPath}' is not a {ReamPaths.Extension} file.", nameof(destReamPath));

        var reamFile = ReadSource(source);
        string sourceRoot = ReamPaths.DataRootOf(source, reamFile.DataFolder ?? ReamPaths.DefaultDataFolder(source));

        string dest = Path.GetFullPath(destReamPath);
        string destDirectory = Path.GetDirectoryName(dest)!;
        string destDataFolder = ReamPaths.DefaultDataFolder(dest);
        string destRoot = ReamPaths.DataRootOf(dest, destDataFolder);

        if (Same(dest, source))
            throw new IOException($"Can't copy '{source}' onto itself.");
        if (ReamPaths.IsOccupied(dest))
            throw new IOException($"'{dest}' can't be used: it, or its data folder '{destRoot}', already exists.");
        if (!Directory.Exists(destDirectory))
            throw new DirectoryNotFoundException($"The folder '{destDirectory}' does not exist.");
        RefuseInsideSource(sourceRoot, source, destDirectory, destRoot);

        var workspaceFolders = Directory.Exists(sourceRoot)
            ? Directory.GetDirectories(sourceRoot)
                .Where(d => Path.GetFileName(d).StartsWith(WorkspaceFolderPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        string destTemp = dest + ".tmp";
        bool tempExisted = File.Exists(destTemp);
        bool createdRoot = false;

        try
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destRoot);
            createdRoot = true;

            foreach (var workspace in workspaceFolders)
                CopyDirectory(workspace, Path.Combine(destRoot, Path.GetFileName(workspace)), cancellation);

            cancellation.ThrowIfCancellationRequested();
            reamFile.DataFolder = destDataFolder;
            AtomicFile.WriteAllText(dest, JsonSerializer.Serialize(reamFile, JsonDefaults.Options));
            return dest;
        }
        catch
        {
            Undo(dest, destTemp, tempExisted, destRoot, createdRoot);
            throw;
        }
    }

    private static ReamFile ReadSource(string source)
    {
        ReamFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ReamFile>(File.ReadAllText(source), JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{source}' is not a readable ream file.", ex);
        }

        if (file is null) throw new InvalidDataException($"'{source}' is not a readable ream file.");
        if (file.SchemaVersion > SchemaVersion)
            throw new InvalidDataException($"'{source}' was written by a newer version of Ream and can't be copied safely.");
        if (file.DataFolder is not null && !ReamPaths.IsValidDataFolder(file.DataFolder))
            throw new InvalidDataException($"'{source}' names a data folder ('{file.DataFolder}') that is not a plain folder name.");

        return file;
    }

    /// <summary>
    /// A copy must not land inside what it is copying. When the source's data lives beside the .ream ("."), the new files may sit
    /// in that same folder; only its workspace folders are off limits (and a new data folder must not look like one, or the source
    /// would adopt it as a workspace).
    /// </summary>
    private static void RefuseInsideSource(string sourceRoot, string source, string destDirectory, string destRoot)
    {
        bool sharesFolderWithReam = Same(sourceRoot, Path.GetDirectoryName(source)!);

        if (!sharesFolderWithReam)
        {
            if (Inside(destDirectory, sourceRoot))
                throw new IOException($"Can't copy a ream into its own data folder '{sourceRoot}'.");
            return;
        }

        if (Directory.Exists(sourceRoot))
        {
            foreach (var workspace in Directory.GetDirectories(sourceRoot, WorkspaceFolderPrefix + "*"))
                if (Inside(destDirectory, workspace))
                    throw new IOException($"Can't copy a ream into its own workspace folder '{workspace}'.");
        }

        if (Same(destDirectory, sourceRoot)
            && Path.GetFileName(destRoot).StartsWith(WorkspaceFolderPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"'{Path.GetFileName(destRoot)}' would look like a workspace folder of the ream it is copied from.");
    }

    private static void CopyDirectory(string from, string to, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(to);

        foreach (var file in Directory.GetFiles(from))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

            cancellation.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)), cancellation);
    }

    /// <summary>Removes what this call made, and only that: nothing of this was there when the copy began.</summary>
    private static void Undo(string dest, string destTemp, bool tempExisted, string destRoot, bool createdRoot)
    {
        TryDelete(() => File.Delete(dest));
        if (!tempExisted) TryDelete(() => File.Delete(destTemp));
        if (createdRoot) TryDelete(() => Directory.Delete(destRoot, recursive: true));
    }

    private static void TryDelete(Action delete)
    {
        try { delete(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or somewhere below it.</summary>
    private static bool Inside(string path, string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
