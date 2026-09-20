using System.ComponentModel;
using System.Security;
using Ream.Persistence.Storage;

namespace Ream.App.Services.FileBrowser;

/// <summary>
/// The state and rules of the file browser dialog, with no UI in it: where it is, what is listed, what is selected, and
/// whether the name box holds something that can be accepted. Everything on disk is reached through <see cref="IFileSystem"/>.
/// </summary>
internal sealed partial class FileBrowserModel : INotifyPropertyChanged
{
    private readonly IFileSystem _fileSystem;
    private readonly List<string> _history = [];
    private int _historyIndex;

    private string _currentDirectory = "";
    private string? _parent;
    private IReadOnlyList<Breadcrumb> _breadcrumbs = [];
    private IReadOnlyList<FileEntry> _listing = [];
    private IReadOnlyList<FileEntry> _entries = [];
    private string? _listingError;
    private IReadOnlyList<BrowserPlace> _places;
    private string _fileName = "";
    private FileEntry? _selected;
    private bool _showAllFiles;
    private SortColumn _sortBy = SortColumn.Name;
    private bool _sortDescending;

    public FileBrowserModel(IFileSystem fileSystem, BrowserMode mode, string initialDirectory, string? suggestedName = null)
    {
        _fileSystem = fileSystem;
        Mode = mode;
        _places = fileSystem.Places();

        string start = FirstExistingDirectory(initialDirectory);
        _history.Add(start);
        Show(start);

        if (mode == BrowserMode.Save && suggestedName is not null) _fileName = suggestedName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BrowserMode Mode { get; }

    /// <summary>The full path of the folder shown.</summary>
    public string CurrentDirectory => _currentDirectory;

    /// <summary>From the drive root down to the current folder ("C:\", "Users", ...).</summary>
    public IReadOnlyList<Breadcrumb> Breadcrumbs => _breadcrumbs;

    /// <summary>What the list shows: folders first, then files, in the chosen order.</summary>
    public IReadOnlyList<FileEntry> Entries => _entries;

    public IReadOnlyList<BrowserPlace> Places => _places;

    /// <summary>Why the folder cannot be shown (access denied, gone), else null. Entries is empty while it is set.</summary>
    public string? ListingError => _listingError;

    /// <summary>False lists only .ream files (folders always show); true is the "All files" filter.</summary>
    public bool ShowAllFiles
    {
        get => _showAllFiles;
        set
        {
            if (_showAllFiles == value) return;
            _showAllFiles = value;
            Raise(nameof(ShowAllFiles));
            Rearrange();
        }
    }

    public SortColumn SortBy
    {
        get => _sortBy;
        set
        {
            if (_sortBy == value) return;
            _sortBy = value;
            Raise(nameof(SortBy));
            Rearrange();
        }
    }

    /// <summary>Reverses the column's natural order: Name is A to Z, Modified is newest first. Folders stay ahead of files.</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (_sortDescending == value) return;
            _sortDescending = value;
            Raise(nameof(SortDescending));
            Rearrange();
        }
    }

    /// <summary>A column header click: the same column flips the direction, another column starts in its natural order.</summary>
    public void SortOn(SortColumn column)
    {
        if (column == _sortBy)
        {
            SortDescending = !_sortDescending;
            return;
        }

        _sortDescending = false;
        Raise(nameof(SortDescending));
        SortBy = column;
    }

    /// <summary>The name box.</summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            value ??= "";
            if (_fileName == value) return;
            _fileName = value;
            Raise(nameof(FileName));
        }
    }

    /// <summary>The highlighted list item. Selecting a file copies its name into <see cref="FileName"/>; a folder leaves it alone.</summary>
    public FileEntry? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Raise(nameof(Selected));
            if (value is { IsDirectory: false }) FileName = value.Name;
        }
    }

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex < _history.Count - 1;

    public bool CanGoUp => _parent is not null;

    // ----- Navigation -----

    /// <summary>Goes to an existing folder and records it in the history. False, and nothing changes, if it does not exist.</summary>
    public bool Navigate(string path)
    {
        if (!TryFullPath(path, out string full) || !_fileSystem.DirectoryExists(full)) return false;
        if (SamePath(full, _currentDirectory)) return true;

        Transition(() =>
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(full);
            _historyIndex++;
            Show(full);
        });
        return true;
    }

    public void Back()
    {
        if (!CanGoBack) return;
        Transition(() => Show(_history[--_historyIndex]));
    }

    public void Forward()
    {
        if (!CanGoForward) return;
        Transition(() => Show(_history[++_historyIndex]));
    }

    public void Up()
    {
        if (_parent is { } parent) Navigate(parent);
    }

    /// <summary>The address box. A folder is entered; a file's folder is entered and the file selected; anything else is Invalid.</summary>
    public NavigateResult NavigateToText(string text)
    {
        string cleaned = CleanText(text);
        if (cleaned.Length == 0) return new NavigateResult(NavigateOutcome.Invalid, "Type a folder path.");

        if (!TryResolve(cleaned, out string full))
            return new NavigateResult(NavigateOutcome.Invalid, $"\"{cleaned}\" isn't a valid path.");

        if (_fileSystem.DirectoryExists(full))
            return Navigate(full)
                ? new NavigateResult(NavigateOutcome.Navigated)
                : new NavigateResult(NavigateOutcome.Invalid, $"Ream can't find \"{cleaned}\".");

        if (_fileSystem.FileExists(full) && _fileSystem.ParentOf(full) is { } folder && Navigate(folder))
        {
            Selected = _listing.FirstOrDefault(e => !e.IsDirectory && SamePath(e.FullPath, full))
                ?? new FileEntry(Path.GetFileName(full), full, false, 0, default);
            return new NavigateResult(NavigateOutcome.SelectedFile);
        }

        return new NavigateResult(NavigateOutcome.Invalid, $"Ream can't find \"{cleaned}\". Check the path and try again.");
    }

    /// <summary>
    /// Double click / Enter on a list item. A folder is entered (NavigatedInstead). A file is selected and then accepted as if
    /// its name had been typed and confirmed, so in Open mode a ream is Accepted and in Save mode an existing one is Rejected.
    /// </summary>
    public AcceptResult OpenEntry(FileEntry entry)
    {
        if (entry.IsDirectory)
            return Navigate(entry.FullPath)
                ? AcceptResult.Navigated(_currentDirectory)
                : AcceptResult.Reject($"\"{entry.Name}\" doesn't exist any more.");

        Selected = entry;
        return TryAccept();
    }

    /// <summary>Makes a folder in the current one, then lists again with the new folder selected. False with a message if it can't be made.</summary>
    public bool CreateFolder(string name, out string? error)
    {
        name = (name ?? "").Trim();
        error = NameRules.Problem(name, "folder", NameRules.MaxFolderNameLength);
        if (error is not null) return false;

        if (!_fileSystem.DirectoryExists(_currentDirectory))
        {
            error = "This folder doesn't exist any more.";
            return false;
        }

        string path = _fileSystem.Combine(_currentDirectory, name);
        if (_fileSystem.DirectoryExists(path) || _fileSystem.FileExists(path))
        {
            error = $"There is already a folder or file called \"{name}\" here.";
            return false;
        }

        try
        {
            _fileSystem.CreateDirectory(path);
        }
        catch (UnauthorizedAccessException)
        {
            error = "Ream isn't allowed to make a folder here.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or SecurityException or NotSupportedException)
        {
            error = $"Ream couldn't make the folder. {ex.Message}";
            return false;
        }

        Refresh();
        Selected = _listing.FirstOrDefault(e => e.IsDirectory && SamePath(e.FullPath, path));
        return true;
    }

    /// <summary>Reads the folder (and the places) again. The selection stays if the item is still there.</summary>
    public void Refresh()
    {
        string? keep = _selected?.FullPath;
        string? error = _listingError;

        _places = _fileSystem.Places();
        ReadListing();
        Raise(nameof(Places));
        Raise(nameof(Entries));
        if (error != _listingError) Raise(nameof(ListingError));

        Selected = keep is null ? null : _listing.FirstOrDefault(e => SamePath(e.FullPath, keep));
    }

    // ----- Internals -----

    private void Show(string directory)
    {
        _currentDirectory = directory;
        _parent = _fileSystem.ParentOf(directory);
        _breadcrumbs = BuildBreadcrumbs(directory);
        ReadListing();
    }

    /// <summary>Runs a move to another folder and tells the UI what changed (including dropping the selection).</summary>
    private void Transition(Action move)
    {
        bool back = CanGoBack, forward = CanGoForward, up = CanGoUp;
        string? error = _listingError;
        FileEntry? selected = _selected;

        move();

        _selected = null;
        if (selected is not null)
        {
            Raise(nameof(Selected));
            // A name that came from selecting a file in the folder we just left would now point at nothing.
            if (!selected.IsDirectory && _fileName == selected.Name) FileName = "";
        }

        Raise(nameof(CurrentDirectory));
        Raise(nameof(Breadcrumbs));
        Raise(nameof(Entries));
        if (back != CanGoBack) Raise(nameof(CanGoBack));
        if (forward != CanGoForward) Raise(nameof(CanGoForward));
        if (up != CanGoUp) Raise(nameof(CanGoUp));
        if (error != _listingError) Raise(nameof(ListingError));
    }

    private void ReadListing()
    {
        _listingError = null;
        try
        {
            _listing = _fileSystem.List(_currentDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            _listing = [];
            _listingError = "Ream isn't allowed to open this folder.";
        }
        catch (DirectoryNotFoundException)
        {
            _listing = [];
            _listingError = "This folder doesn't exist any more.";
        }
        catch (Exception ex) when (ex is IOException or SecurityException or NotSupportedException or ArgumentException)
        {
            _listing = [];
            _listingError = $"This folder can't be read. {ex.Message}";
        }

        _entries = Arrange(_listing);
    }

    private void Rearrange()
    {
        _entries = Arrange(_listing);
        Raise(nameof(Entries));
    }

    private FileEntry[] Arrange(IReadOnlyList<FileEntry> listing)
    {
        var entries = new List<FileEntry>(listing.Count);
        foreach (FileEntry entry in listing)
        {
            if (entry.IsDirectory || _showAllFiles || ReamPaths.IsReamFile(entry.Name)) entries.Add(entry);
        }

        entries.Sort(_sortBy == SortColumn.Name
            ? (a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : ByName(a, b) * (_sortDescending ? -1 : 1)
            : (a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : ByNewest(a, b) * (_sortDescending ? -1 : 1));
        return [.. entries];
    }

    private static int ByName(FileEntry a, FileEntry b) => NaturalCompare(a.Name, b.Name);

    private static int ByNewest(FileEntry a, FileEntry b)
    {
        int byDate = b.Modified.CompareTo(a.Modified);
        return byDate != 0 ? byDate : ByName(a, b);
    }

    /// <summary>Case-insensitive, with runs of digits compared as numbers ("note 2" before "note 10").</summary>
    internal static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (IsDigit(a[i]) && IsDigit(b[j]))
            {
                int startA = i, startB = j;
                while (i < a.Length && IsDigit(a[i])) i++;
                while (j < b.Length && IsDigit(b[j])) j++;

                ReadOnlySpan<char> numberA = a.AsSpan(startA, i - startA).TrimStart('0');
                ReadOnlySpan<char> numberB = b.AsSpan(startB, j - startB).TrimStart('0');
                if (numberA.Length != numberB.Length) return numberA.Length.CompareTo(numberB.Length);

                int byNumber = numberA.CompareTo(numberB, StringComparison.Ordinal);
                if (byNumber != 0) return byNumber;
                continue;
            }

            int byChar = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
            if (byChar != 0) return byChar;
            i++;
            j++;
        }

        int byRest = (a.Length - i).CompareTo(b.Length - j);
        return byRest != 0 ? byRest : string.CompareOrdinal(a, b);

        static bool IsDigit(char c) => c is >= '0' and <= '9';
    }

    private IReadOnlyList<Breadcrumb> BuildBreadcrumbs(string directory)
    {
        var crumbs = new List<Breadcrumb>();
        string? path = directory;
        for (int guard = 0; path is not null && guard < 256; guard++)
        {
            string? parent = _fileSystem.ParentOf(path);
            string name = parent is null ? path : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            crumbs.Add(new Breadcrumb(name, path));
            path = parent;
        }

        crumbs.Reverse();
        return crumbs;
    }

    private string FirstExistingDirectory(string requested)
    {
        if (TryFullPath(requested, out string full) && _fileSystem.DirectoryExists(full)) return full;

        foreach (BrowserPlace place in _places.Where(p => p.Group == WindowsFileSystem.QuickAccess))
        {
            if (_fileSystem.DirectoryExists(place.Path)) return place.Path;
        }

        foreach (BrowserPlace place in _places.Where(p => p.Group == WindowsFileSystem.ThisPc))
        {
            if (_fileSystem.DirectoryExists(place.Path)) return place.Path;
        }

        return TryFullPath(requested, out full) ? full : requested;
    }

    /// <summary>Trims spaces and one pair of quotes, expands %VARIABLES%.</summary>
    private static string CleanText(string? text)
    {
        string cleaned = (text ?? "").Trim().Trim('"').Trim();
        return cleaned.Length == 0 ? "" : Environment.ExpandEnvironmentVariables(cleaned).Trim();
    }

    /// <summary>Text from a box: a path as typed, or a name relative to the current folder, as a full path with no trailing separator.</summary>
    private bool TryResolve(string text, out string full) =>
        TryFullPath(Path.IsPathRooted(text) ? text : _fileSystem.Combine(_currentDirectory, text), out full);

    private static bool TryFullPath(string? path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (path.Length == 2 && path[1] == ':' && char.IsAsciiLetter(path[0])) path += '\\';
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
