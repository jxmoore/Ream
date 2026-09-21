using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Ream.App.Services.FileBrowser;

namespace Ream.App.Views;

/// <summary>
/// Ream's own Open / New / Save As dialog: the drawn title bar and theme colors instead of the native file dialogs. All of the
/// rules (what may be accepted, where a folder name goes, sorting) live in <see cref="FileBrowserModel"/>; this is the view.
/// </summary>
internal sealed partial class FileBrowserWindow : Window
{
    private readonly FileBrowserModel _model;
    private bool _syncing;
    private bool _finished;

    public FileBrowserWindow(FileBrowserModel model, string title, string acceptText)
    {
        _model = model;
        InitializeComponent();
        Title = title;
        AcceptButton.Content = acceptText;
        FileNameBox.Text = model.FileName;

        PlacesList.ItemsSource = GroupedPlaces(model.Places);

        InputBindings.Add(Bind(Key.Left, ModifierKeys.Alt, _model.Back));
        InputBindings.Add(Bind(Key.Right, ModifierKeys.Alt, _model.Forward));
        InputBindings.Add(Bind(Key.Up, ModifierKeys.Alt, _model.Up));
        InputBindings.Add(Bind(Key.F5, ModifierKeys.None, _model.Refresh));
        InputBindings.Add(Bind(Key.N, ModifierKeys.Control | ModifierKeys.Shift, BeginNewFolder));
        InputBindings.Add(Bind(Key.F4, ModifierKeys.None, BeginEditAddress));
        InputBindings.Add(Bind(Key.L, ModifierKeys.Control, BeginEditAddress));
        InputBindings.Add(Bind(Key.D, ModifierKeys.Alt, BeginEditAddress));

        model.PropertyChanged += OnModelChanged;
        Sync();

        // Something inside always has keyboard focus, so the keys above keep reaching the window.
        FocusManager.SetFocusedElement(this, MainFocus);
        Loaded += (_, _) => MainFocus.Focus();
        Activated += (_, _) =>
        {
            if (FocusManager.GetFocusedElement(this) is null) MainFocus.Focus();
        };
    }

    internal FileBrowserModel Model => _model;

    /// <summary>The full path the user chose, or null if the dialog was cancelled or closed.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>The name box for a Save (you type a name), the list for an Open (you pick a ream).</summary>
    private IInputElement MainFocus => _model.Mode == BrowserMode.Save ? FileNameBox : FileList;

    private static KeyBinding Bind(Key key, ModifierKeys modifiers, Action run) =>
        new(new RelayCommand(run), key, modifiers);

    // ----- Keeping the view in step with the model -----

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void Sync()
    {
        BackButton.IsEnabled = _model.CanGoBack;
        ForwardButton.IsEnabled = _model.CanGoForward;
        UpButton.IsEnabled = _model.CanGoUp;

        if (!ReferenceEquals(Crumbs.ItemsSource, _model.Breadcrumbs))
        {
            Crumbs.ItemsSource = _model.Breadcrumbs;
            ErrorText.Text = "";
            Dispatcher.BeginInvoke(new Action(CrumbScroll.ScrollToRightEnd));
        }

        _syncing = true;
        try
        {
            if (!ReferenceEquals(FileList.ItemsSource, _model.Entries)) FileList.ItemsSource = _model.Entries;
            SelectInList(_model.Selected);
            SelectPlace(_model.CurrentDirectory);
        }
        finally
        {
            _syncing = false;
        }

        ListingErrorText.Text = _model.ListingError ?? "";
        ListingErrorText.Visibility = _model.ListingError is null ? Visibility.Collapsed : Visibility.Visible;
        bool empty = _model.ListingError is null && _model.Entries.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _model.ShowAllFiles ? "This folder is empty." : "No reams in this folder.";

        NameArrow.Text = _model.SortBy == SortColumn.Name ? ArrowFor(_model.SortDescending) : "";
        DateArrow.Text = _model.SortBy == SortColumn.Modified ? ArrowFor(_model.SortDescending) : "";

        if (FileNameBox.Text != _model.FileName) FileNameBox.Text = _model.FileName;

        int filter = _model.ShowAllFiles ? 1 : 0;
        if (FilterBox.SelectedIndex != filter) FilterBox.SelectedIndex = filter;
    }

    private static string ArrowFor(bool descending) => descending ? "" : "";

    private void SelectInList(FileEntry? selected)
    {
        var match = selected is null
            ? null
            : _model.Entries.FirstOrDefault(e => string.Equals(e.FullPath, selected.FullPath, StringComparison.OrdinalIgnoreCase));
        if (Equals(FileList.SelectedItem, match)) return;

        FileList.SelectedItem = match;
        if (match is not null) FileList.ScrollIntoView(match);
    }

    private static System.ComponentModel.ICollectionView GroupedPlaces(IReadOnlyList<BrowserPlace> places)
    {
        var view = new System.Windows.Data.ListCollectionView(places.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserPlace.Group)));
        return view;
    }

    private void SelectPlace(string directory)
    {
        var match = _model.Places.FirstOrDefault(p =>
            string.Equals(p.Path.TrimEnd('\\'), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (!Equals(PlacesList.SelectedItem, match)) PlacesList.SelectedItem = match;
    }

    // ----- Toolbar -----

    private void OnBack(object sender, RoutedEventArgs e) => _model.Back();

    private void OnForward(object sender, RoutedEventArgs e) => _model.Forward();

    private void OnUp(object sender, RoutedEventArgs e) => _model.Up();

    private void OnRefresh(object sender, RoutedEventArgs e) => _model.Refresh();

    private void OnCrumbClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is string path) _model.Navigate(path);
    }

    private void OnPlaceClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: BrowserPlace place } && !_model.Navigate(place.Path))
            ShowError($"Ream can't open \"{place.Name}\".");
    }

    private void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PlacesList.SelectedItem is not BrowserPlace place) return;
        if (!_model.Navigate(place.Path)) ShowError($"Ream can't open \"{place.Name}\".");
    }

    // ----- The address bar -----

    private void OnAddressBarClick(object sender, MouseButtonEventArgs e)
    {
        if (AddressBox.Visibility == Visibility.Visible) return;
        BeginEditAddress();
        e.Handled = true;
    }

    internal bool IsEditingAddress => AddressBox.Visibility == Visibility.Visible;

    /// <summary>Turns the breadcrumbs into a text box holding the path, to type or paste another one.</summary>
    internal void BeginEditAddress()
    {
        AddressBox.Text = _model.CurrentDirectory;
        AddressBox.Visibility = Visibility.Visible;
        CrumbScroll.Visibility = Visibility.Collapsed;
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void EndEditAddress()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbScroll.Visibility = Visibility.Visible;
    }

    /// <summary>Enter in the address box: go where the text says. On a bad path the box stays, with the reason under the list.</summary>
    internal bool CommitAddress()
    {
        var result = _model.NavigateToText(AddressBox.Text);
        if (!result.Succeeded)
        {
            ShowError(result.Error);
            return false;
        }

        EndEditAddress();
        MainFocus.Focus();
        return true;
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitAddress();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEditAddress();
            MainFocus.Focus();
            e.Handled = true;
        }
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsEditingAddress) return;

        EndEditAddress();
        if (e.NewFocus is null) Dispatcher.BeginInvoke(new Action(() => MainFocus.Focus()));
    }

    // ----- New folder -----

    private void OnNewFolderClick(object sender, RoutedEventArgs e) => BeginNewFolder();

    internal void BeginNewFolder()
    {
        NewFolderBox.Text = "New folder";
        NewFolderError.Visibility = Visibility.Collapsed;
        NewFolderPopup.IsOpen = true;
    }

    private void OnNewFolderOpened(object? sender, EventArgs e)
    {
        NewFolderBox.Focus();
        NewFolderBox.SelectAll();
    }

    /// <summary>Makes the folder named in the popup; on a problem the popup stays open and says what it is.</summary>
    internal bool CommitNewFolder()
    {
        if (!_model.CreateFolder(NewFolderBox.Text, out string? error))
        {
            NewFolderError.Text = error;
            NewFolderError.Visibility = Visibility.Visible;
            return false;
        }

        NewFolderPopup.IsOpen = false;
        MainFocus.Focus();
        return true;
    }

    private void OnNewFolderKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitNewFolder();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NewFolderPopup.IsOpen = false;
            MainFocus.Focus();
            e.Handled = true;
        }
    }

    // ----- The list -----

    private void OnEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _model.Selected = FileList.SelectedItem as FileEntry;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: FileEntry entry } item && ItemsControl.ItemsControlFromItemContainer(item) == FileList)
        {
            ActivateEntry(entry);
            e.Handled = true;
        }
    }

    /// <summary>Double click / Enter on a list item: a folder is entered, a ream is chosen.</summary>
    internal void ActivateEntry(FileEntry entry) => Apply(_model.OpenEntry(entry));

    private void OnFileListKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        if (e.Key == Key.Enter && FileList.SelectedItem is FileEntry entry)
        {
            ActivateEntry(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            _model.Up();
            e.Handled = true;
        }
    }

    private void OnSortName(object sender, RoutedEventArgs e) => _model.SortOn(SortColumn.Name);

    private void OnSortDate(object sender, RoutedEventArgs e) => _model.SortOn(SortColumn.Modified);

    // ----- The bottom -----

    private void OnFileNameChanged(object sender, TextChangedEventArgs e)
    {
        ErrorText.Text = "";
        _model.FileName = FileNameBox.Text;
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        _model.ShowAllFiles = FilterBox.SelectedIndex == 1;
    }

    private void OnAccept(object sender, RoutedEventArgs e) => Accept();

    /// <summary>The Open / Create / Save button and Enter in the name box.</summary>
    internal void Accept() => Apply(_model.TryAccept());

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(null);

    private void Apply(AcceptResult result)
    {
        switch (result.Outcome)
        {
            case AcceptOutcome.Accepted:
                Finish(result.Path);
                break;
            case AcceptOutcome.NavigatedInstead:
                ErrorText.Text = "";
                FileList.Focus();
                break;
            default:
                ShowError(result.Error);
                if (_model.Mode == BrowserMode.Save)
                {
                    FileNameBox.Focus();
                    FileNameBox.SelectAll();
                }

                break;
        }
    }

    private void ShowError(string? message) => ErrorText.Text = message ?? "";

    /// <summary>Closes the dialog with an answer. Shown modally it returns that answer; a window that was only Show()n is just closed.</summary>
    private void Finish(string? path)
    {
        if (_finished) return;

        _finished = true;
        SelectedPath = path;
        try
        {
            DialogResult = path is not null;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}

/// <summary>Turns a list entry (or a place) into the text and icon of one of its columns.</summary>
internal sealed class EntryColumn : IValueConverter
{
    public static EntryColumn Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (parameter as string, value) switch
    {
        ("glyph", FileEntry { IsDirectory: true }) => "",
        ("glyph", FileEntry) => "",
        ("date", FileEntry { Modified.Year: > 1900 } entry) => entry.Modified.ToString("g", culture),
        ("size", FileEntry { IsDirectory: false } entry) => SizeText(entry.Size),
        ("place", BrowserPlace { Group: WindowsFileSystem.ThisPc }) => "",
        ("place", BrowserPlace) => "",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{(bytes + 1023) / 1024:N0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.#} GB",
    };
}
