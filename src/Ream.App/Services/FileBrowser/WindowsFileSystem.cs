namespace Ream.App.Services.FileBrowser;

internal sealed class WindowsFileSystem : IFileSystem
{
    public const string QuickAccess = "Quick access";
    public const string ThisPc = "This PC";

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<FileEntry> List(string directory)
    {
        var result = new List<FileEntry>();
        foreach (FileSystemInfo info in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            try
            {
                FileAttributes attributes = info.Attributes;
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                long size = !isDirectory && info is FileInfo file ? file.Length : 0;
                result.Add(new FileEntry(info.Name, info.FullName, isDirectory, size, info.LastWriteTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // an item that vanished or cannot be inspected is just left out
            }
        }

        return result;
    }

    public IReadOnlyList<BrowserPlace> Places()
    {
        var places = new List<BrowserPlace>();
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AddQuick(places, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddQuick(places, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (profile.Length > 0) AddQuick(places, "Downloads", Path.Combine(profile, "Downloads"));
        AddQuick(places, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddQuick(places, "OneDrive", Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer"));

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network) || !drive.IsReady) continue;
                    places.Add(new BrowserPlace(DriveName(drive), drive.RootDirectory.FullName, ThisPc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // a drive that stops answering is skipped
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return places;
    }

    public string? ParentOf(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));

    public string Combine(string directory, string name) => Path.Combine(directory, name);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    private static void AddQuick(List<BrowserPlace> places, string name, string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            if (places.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            places.Add(new BrowserPlace(name, path, QuickAccess));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string DriveName(DriveInfo drive)
    {
        string letter = drive.Name.TrimEnd('\\');
        string label = drive.VolumeLabel;
        if (!string.IsNullOrWhiteSpace(label)) return $"{label} ({letter})";

        return drive.DriveType switch
        {
            DriveType.Removable => $"Removable Disk ({letter})",
            DriveType.Network => $"Network Drive ({letter})",
            _ => $"Local Disk ({letter})",
        };
    }
}
