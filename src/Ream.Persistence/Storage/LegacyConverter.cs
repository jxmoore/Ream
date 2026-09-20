using System.Globalization;
using System.Text.Json;
using Ream.Persistence.Io;
using Ream.Persistence.Json;
using static Ream.Persistence.Storage.StorageConstants;

namespace Ream.Persistence.Storage;

/// <summary>The result of converting an old-format folder: the new .ream file, and the backup made first (null when resuming).</summary>
public sealed record LegacyConversion(string ReamFilePath, string? BackupFolder);

/// <summary>
/// Converts a ream in the old layout (a folder with <c>metadata.json</c> and <c>ws-*/layout.json</c>) to the new one
/// (<c>Foo.ream</c> and <c>ws-*/layout.reamlayout</c>) in place. The folder is copied to a sibling backup first and the
/// backup is never deleted. Note files, images and the trash are not touched. Safe to run again: it only finishes what
/// an interrupted run left, and does nothing once converted.
/// </summary>
public static class LegacyConverter
{
    private const string FallbackName = "Ream";

    /// <summary>
    /// True if the folder has a <c>metadata.json</c> and no .ream file, or has a .ream file but some
    /// <c>ws-*/layout.json</c> still waits to be renamed (an interrupted conversion).
    /// </summary>
    public static bool NeedsConversion(string folder)
    {
        if (!Directory.Exists(folder)) return false;

        if (FindReamFile(folder) is null)
            return File.Exists(Path.Combine(folder, LegacyMetadataFileName));

        return PendingLayouts(folder).Count > 0;
    }

    /// <summary>Converts the folder if it needs it; null when there is nothing to do.</summary>
    /// <exception cref="InvalidDataException">The metadata.json was written by a newer version of Ream. Nothing has been changed.</exception>
    /// <exception cref="IOException">The backup could not be made. Nothing has been changed.</exception>
    public static LegacyConversion? ConvertIfNeeded(string folder, DateTime? now = null)
    {
        if (!Directory.Exists(folder)) return null;

        string? existing = FindReamFile(folder);
        if (existing is not null)
        {
            var pending = PendingLayouts(folder);
            if (pending.Count == 0) return null;

            RenameLayouts(pending);
            return new LegacyConversion(existing, null);
        }

        string metadataPath = Path.Combine(folder, LegacyMetadataFileName);
        if (!File.Exists(metadataPath)) return null;

        var stamp = now ?? DateTime.Now;
        var metadata = ReadMetadata(metadataPath);
        if (metadata is not null && metadata.SchemaVersion > SchemaVersion)
            throw new InvalidDataException($"'{metadataPath}' was written by a newer version of Ream (schema {metadata.SchemaVersion}).");

        string backup = MakeBackup(folder, stamp);

        string name = Path.GetFileName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string reamPath = Path.Combine(folder, (ReamPaths.IsValidName(name) ? name : FallbackName) + ReamPaths.Extension);

        // A metadata.json we cannot read becomes a ream with no workspaces; the loader adopts the ws-* folders itself.
        var ream = metadata ?? new ReamFile { Workspaces = [] };
        ream.DataFolder = ReamPaths.SameFolder;
        AtomicFile.WriteAllText(reamPath, JsonSerializer.Serialize(ream, JsonDefaults.Options));

        // The .ream is safely on disk; only now does the old file go.
        if (metadata is null) MoveAside(metadataPath, stamp);
        else File.Delete(metadataPath);

        RenameLayouts(PendingLayouts(folder));
        return new LegacyConversion(reamPath, backup);
    }

    private static string? FindReamFile(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(ReamPaths.IsReamFile)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    /// <summary>Null when the file is not a usable ream: not JSON, or JSON of the wrong shape.</summary>
    private static ReamFile? ReadMetadata(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ReamFile>(File.ReadAllText(path), JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void MoveAside(string path, DateTime stamp)
    {
        string baseName = path + ".corrupt-" + stamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string target = baseName;
        for (int n = 2; File.Exists(target); n++)
            target = $"{baseName} ({n})";

        File.Move(path, target);
    }

    /// <summary>
    /// The workspace folders (direct children named ws-*) whose old layout.json can be renamed now: it exists, and either
    /// there is no layout.reamlayout yet or the one there is readable (then the old one is redundant and just goes).
    /// </summary>
    private static List<string> PendingLayouts(string folder) =>
        Directory.EnumerateDirectories(folder, WorkspaceFolderPrefix + "*")
            .Where(directory =>
            {
                if (!File.Exists(Path.Combine(directory, LegacyLayoutFileName))) return false;
                string current = Path.Combine(directory, LayoutFileName);
                return !File.Exists(current) || IsJson(current);
            })
            .ToList();

    private static void RenameLayouts(List<string> workspaceFolders)
    {
        foreach (string directory in workspaceFolders)
        {
            string legacy = Path.Combine(directory, LegacyLayoutFileName);
            string current = Path.Combine(directory, LayoutFileName);

            if (File.Exists(current)) File.Delete(legacy);
            else File.Move(legacy, current);
        }
    }

    private static bool IsJson(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the whole folder to a sibling "&lt;name&gt;-backup-&lt;stamp&gt;". It is built under a ".partial" name and renamed when
    /// complete, so a half-made copy never passes for a backup; a failure removes it and leaves the original alone.
    /// </summary>
    private static string MakeBackup(string folder, DateTime stamp)
    {
        var source = new DirectoryInfo(folder);
        string parent = source.Parent?.FullName
            ?? throw new InvalidOperationException($"'{folder}' has no parent folder to put a backup in.");

        string baseName = $"{source.Name}-backup-{stamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        string target = Path.Combine(parent, baseName);
        for (int n = 2; Directory.Exists(target) || File.Exists(target); n++)
            target = Path.Combine(parent, $"{baseName} ({n})");

        string partial = target + ".partial";
        try
        {
            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
            CopyDirectory(source.FullName, partial);
            Directory.Move(partial, target);
        }
        catch
        {
            try { if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }

        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var directory in new DirectoryInfo(source).EnumerateDirectories())
        {
            // Links are not followed: they could point anywhere, or back into this folder.
            if (directory.LinkTarget is not null) continue;
            CopyDirectory(directory.FullName, Path.Combine(target, directory.Name));
        }
    }
}
