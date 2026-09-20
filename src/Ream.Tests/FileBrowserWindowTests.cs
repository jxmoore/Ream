using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.Services;
using Ream.App.Services.FileBrowser;
using Ream.App.Views;

namespace Ream.Tests;

public class FileBrowserWindowTests
{
    private const string Docs = @"C:\Users\Joe\Documents";
    private const string Desktop = @"C:\Users\Joe\Desktop";

    private static readonly DateTime Jan = new(2026, 1, 1);
    private static readonly DateTime Feb = new(2026, 2, 1);

    /// <summary>Documents holds two reams (with their data folders), a plain folder and two other files.</summary>
    private static FakeFileSystem Disk()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Desktop)
            .AddDirectory(@"D:\")
            .AddFile(Docs + @"\Notes.ream", modified: Jan)
            .AddDirectory(Docs + @"\Notes")
            .AddFile(Docs + @"\Work.ream", modified: Feb)
            .AddDirectory(Docs + @"\Work")
            .AddDirectory(Docs + @"\Sub")
            .AddFile(Docs + @"\Sub\Inner.ream")
            .AddFile(Docs + @"\readme.txt")
            .AddPlace("Desktop", Desktop, WindowsFileSystem.QuickAccess)
            .AddPlace("Documents", Docs, WindowsFileSystem.QuickAccess)
            .AddPlace("Local Disk (C:)", @"C:\", WindowsFileSystem.ThisPc)
            .AddPlace("Local Disk (D:)", @"D:\", WindowsFileSystem.ThisPc);
        return fs;
    }

    private static FileBrowserWindow Show(FakeFileSystem fs, BrowserMode mode, string directory,
        string? suggested, out FileBrowserModel model)
    {
        model = new FileBrowserModel(fs, mode, directory, suggested);
        return Show(model, mode == BrowserMode.Open ? "Open ream" : "New ream", mode == BrowserMode.Open ? "Open" : "Create");
    }

    private static FileBrowserWindow Show(FileBrowserModel model, string title, string acceptText)
    {
        var window = new FileBrowserWindow(model, title, acceptText)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        Ui.Settle();
        return window;
    }

    private static T Named<T>(Window window, string name) where T : class => (T)window.FindName(name);

    private static void Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Ui.Settle();
    }

    private static string[] ListNames(FileBrowserWindow window) =>
        Named<ListBox>(window, "FileList").Items.Cast<FileEntry>().Select(e => e.Name).ToArray();

    private static void Press(FileBrowserWindow window, Key key, ModifierKeys modifiers)
    {
        var binding = Assert.Single(window.InputBindings.OfType<KeyBinding>(), b => b.Key == key && b.Modifiers == modifiers);
        binding.Command.Execute(null);
        Ui.Settle();
    }

    // ----- Frame, places, list -----

    [Fact]
    public void ItWearsTheAppTitleBar_WithItsTitle_AndACloseButton() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            window.ApplyTemplate();

            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal("Open ream", ((TextBlock)window.Template.FindName("PART_TitleText", window)).Text);
            Assert.NotNull(window.Template.FindName("PART_TitleBar", window));
            Assert.NotNull(window.Template.FindName("PART_CloseButton", window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void AcceptButton_CarriesTheGivenText() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, "x", out _);
        try
        {
            Assert.Equal("Create", Named<Button>(window, "AcceptButton").Content);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PlacesAreListedInTheirGroups_AndTheCurrentFolderIsSelected() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            var places = Named<ListBox>(window, "PlacesList");

            Assert.Equal(4, places.Items.Count);
            Assert.Equal("Documents", ((BrowserPlace)places.SelectedItem).Name);
            Assert.Equal(new[] { WindowsFileSystem.QuickAccess, WindowsFileSystem.ThisPc },
                places.Items.Cast<BrowserPlace>().Select(p => p.Group).Distinct());

            model.Navigate(Desktop);
            Ui.Settle();
            Assert.Equal("Desktop", ((BrowserPlace)places.SelectedItem).Name);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheList_ShowsFoldersFirst_ThenReams_AndHidesOtherFiles() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            Assert.Equal(["Notes", "Sub", "Work", "Notes.ream", "Work.ream"], ListNames(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheFilter_CanShowEveryFile() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            Named<ComboBox>(window, "FilterBox").SelectedIndex = 1;
            Ui.Settle();

            Assert.True(model.ShowAllFiles);
            Assert.Contains("readme.txt", ListNames(window));

            Named<ComboBox>(window, "FilterBox").SelectedIndex = 0;
            Ui.Settle();
            Assert.DoesNotContain("readme.txt", ListNames(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ClickingAColumnHeader_SortsTheList() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            Invoke(Named<Button>(window, "NameHeader"));
            Assert.True(model.SortDescending);
            Assert.Equal(["Work", "Sub", "Notes", "Work.ream", "Notes.ream"], ListNames(window));

            Invoke(Named<Button>(window, "DateHeader"));
            Assert.Equal(SortColumn.Modified, model.SortBy);
            Assert.False(string.IsNullOrEmpty(Named<TextBlock>(window, "DateArrow").Text));
            Assert.True(string.IsNullOrEmpty(Named<TextBlock>(window, "NameArrow").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void AFolderThatCannotBeRead_SaysSo_InsteadOfListing() => Ui.Run(() =>
    {
        var fs = Disk().Deny(Docs);
        var window = Show(fs, BrowserMode.Open, Docs, null, out var model);
        try
        {
            var error = Named<TextBlock>(window, "ListingErrorText");

            Assert.NotNull(model.ListingError);
            Assert.Equal(Visibility.Visible, error.Visibility);
            Assert.Equal(model.ListingError, error.Text);
            Assert.Empty(ListNames(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void AnEmptyFolder_SaysItIsEmpty() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Desktop, null, out _);
        try
        {
            Assert.Equal(Visibility.Visible, Named<TextBlock>(window, "EmptyText").Visibility);
        }
        finally { window.Close(); }
    });

    // ----- Moving around -----

    [Fact]
    public void TheBreadcrumbs_FollowTheFolder_AndClickingOneGoesThere() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            var crumbs = Named<ItemsControl>(window, "Crumbs");
            Assert.Equal(model.Breadcrumbs.Count, crumbs.Items.Count);

            var users = Ui.Descendants<Button>(crumbs).First(b => b.Tag is string p && p.EndsWith(@"\Users", StringComparison.OrdinalIgnoreCase));
            Invoke(users);

            Assert.Equal(@"C:\Users", model.CurrentDirectory);
            Assert.Equal(model.Breadcrumbs.Count, crumbs.Items.Count);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void BackForwardUp_AreEnabledByTheHistory_AndDrivenByTheButtons() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            var back = Named<Button>(window, "BackButton");
            var forward = Named<Button>(window, "ForwardButton");
            var up = Named<Button>(window, "UpButton");

            Assert.False(back.IsEnabled);
            Assert.False(forward.IsEnabled);
            Assert.True(up.IsEnabled);

            Invoke(up);
            Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);
            Assert.True(back.IsEnabled);

            Invoke(back);
            Assert.Equal(Docs, model.CurrentDirectory);
            Assert.True(forward.IsEnabled);

            Invoke(forward);
            Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void UpIsDisabled_AtADriveRoot() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, @"D:\", null, out _);
        try
        {
            Assert.False(Named<Button>(window, "UpButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ClickingAPlace_GoesThere() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            var places = Named<ListBox>(window, "PlacesList");
            places.SelectedItem = places.Items.Cast<BrowserPlace>().First(p => p.Name == "Desktop");
            Ui.Settle();

            Assert.Equal(Desktop, model.CurrentDirectory);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheAddressBar_TurnsIntoATextBox_AndTypedPathGoesThere() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            var box = Named<TextBox>(window, "AddressBox");
            Assert.Equal(Visibility.Collapsed, box.Visibility);

            window.BeginEditAddress();
            Ui.Settle();
            Assert.Equal(Visibility.Visible, box.Visibility);
            Assert.Equal(Docs, box.Text);

            box.Text = Desktop;
            Assert.True(window.CommitAddress());
            Ui.Settle();

            Assert.Equal(Desktop, model.CurrentDirectory);
            Assert.Equal(Visibility.Collapsed, box.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ABadTypedPath_KeepsTheBoxOpen_AndSaysWhy() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            window.BeginEditAddress();
            Named<TextBox>(window, "AddressBox").Text = @"C:\nowhere\at all";

            Assert.False(window.CommitAddress());

            Assert.Equal(Docs, model.CurrentDirectory);
            Assert.True(window.IsEditingAddress);
            Assert.False(string.IsNullOrEmpty(Named<TextBlock>(window, "ErrorText").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void EscapeInTheAddressBox_GoesBackToTheBreadcrumbs() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            window.BeginEditAddress();
            var box = Named<TextBox>(window, "AddressBox");

            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
            Ui.Settle();

            Assert.False(window.IsEditingAddress);
            Assert.NotNull(FocusManager.GetFocusedElement(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheKeys_DriveTheBrowser() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            Press(window, Key.Up, ModifierKeys.Alt);
            Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);

            Press(window, Key.Left, ModifierKeys.Alt);
            Assert.Equal(Docs, model.CurrentDirectory);

            Press(window, Key.Right, ModifierKeys.Alt);
            Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);

            Press(window, Key.L, ModifierKeys.Control);
            Assert.True(window.IsEditingAddress);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void F5_ReadsTheFolderAgain() => Ui.Run(() =>
    {
        var fs = Disk();
        var window = Show(fs, BrowserMode.Open, Docs, null, out _);
        try
        {
            int before = fs.ListCount;
            Press(window, Key.F5, ModifierKeys.None);

            Assert.True(fs.ListCount > before);
        }
        finally { window.Close(); }
    });

    // ----- New folder -----

    [Fact]
    public void NewFolder_MakesItAndSelectsIt() => Ui.Run(() =>
    {
        var fs = Disk();
        var window = Show(fs, BrowserMode.Open, Docs, null, out var model);
        try
        {
            window.BeginNewFolder();
            Named<TextBox>(window, "NewFolderBox").Text = "Fresh";

            Assert.True(window.CommitNewFolder());
            Ui.Settle();

            Assert.True(fs.DirectoryExists(Docs + @"\Fresh"));
            Assert.Contains("Fresh", ListNames(window));
            Assert.Equal("Fresh", model.Selected?.Name);
            Assert.False(Named<System.Windows.Controls.Primitives.Popup>(window, "NewFolderPopup").IsOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void NewFolder_WithATakenName_StaysOpenAndSaysWhy() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            window.BeginNewFolder();
            Named<TextBox>(window, "NewFolderBox").Text = "Sub";

            Assert.False(window.CommitNewFolder());

            var error = Named<TextBlock>(window, "NewFolderError");
            Assert.Equal(Visibility.Visible, error.Visibility);
            Assert.False(string.IsNullOrEmpty(error.Text));
        }
        finally { window.Close(); }
    });

    // ----- Accepting -----

    [Fact]
    public void Open_DoubleClickingAReam_ChoosesIt() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            window.ActivateEntry(model.Entries.First(e => e.Name == "Work.ream"));
            Ui.Settle();

            Assert.Equal(Docs + @"\Work.ream", window.SelectedPath);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Open_DoubleClickingAFolder_GoesInsideInsteadOfClosing() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            window.ActivateEntry(model.Entries.First(e => e.Name == "Sub"));
            Ui.Settle();

            Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
            Assert.Null(window.SelectedPath);
            Assert.True(window.IsVisible);
            Assert.Contains("Inner.ream", ListNames(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Open_WithNothingChosen_SaysToChooseAReam_AndStaysOpen() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            Invoke(Named<Button>(window, "AcceptButton"));

            Assert.Null(window.SelectedPath);
            Assert.True(window.IsVisible);
            Assert.False(string.IsNullOrEmpty(Named<TextBlock>(window, "ErrorText").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Open_ANonReamFile_IsRefused() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            model.ShowAllFiles = true;
            Ui.Settle();
            window.ActivateEntry(model.Entries.First(e => e.Name == "readme.txt"));
            Ui.Settle();

            Assert.Null(window.SelectedPath);
            Assert.True(window.IsVisible);
            Assert.False(string.IsNullOrEmpty(Named<TextBlock>(window, "ErrorText").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Save_ANewName_GetsTheExtension_AndIsChosen() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, null, out var model);
        try
        {
            Named<TextBox>(window, "FileNameBox").Text = "Fresh";
            Assert.Equal("Fresh", model.FileName);

            Invoke(Named<Button>(window, "AcceptButton"));

            Assert.Equal(Docs + @"\Fresh.ream", window.SelectedPath);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Save_ATakenName_IsRefused_AndTheWindowStaysOpen() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, null, out _);
        try
        {
            Named<TextBox>(window, "FileNameBox").Text = "Notes.ream";
            Invoke(Named<Button>(window, "AcceptButton"));

            Assert.Null(window.SelectedPath);
            Assert.True(window.IsVisible);
            Assert.False(string.IsNullOrEmpty(Named<TextBlock>(window, "ErrorText").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Save_TheErrorClears_WhenTheNameChanges() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, null, out _);
        try
        {
            var name = Named<TextBox>(window, "FileNameBox");
            name.Text = "Notes.ream";
            Invoke(Named<Button>(window, "AcceptButton"));
            Assert.False(string.IsNullOrEmpty(Named<TextBlock>(window, "ErrorText").Text));

            name.Text = "Other";

            Assert.True(string.IsNullOrEmpty(Named<TextBlock>(window, "ErrorText").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Save_ANameThatIsAFolder_GoesIntoIt() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, null, out var model);
        try
        {
            Named<TextBox>(window, "FileNameBox").Text = "Sub";
            Invoke(Named<Button>(window, "AcceptButton"));

            Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
            Assert.Null(window.SelectedPath);
            Assert.True(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Save_StartsWithTheSuggestedName() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, "Untitled", out _);
        try
        {
            Assert.Equal("Untitled", Named<TextBox>(window, "FileNameBox").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ClickingAFile_PutsItsNameInTheNameBox() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            var list = Named<ListBox>(window, "FileList");
            list.SelectedItem = list.Items.Cast<FileEntry>().First(e => e.Name == "Work.ream");
            Ui.Settle();

            Assert.Equal("Work.ream", Named<TextBox>(window, "FileNameBox").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Cancel_ClosesWithNoAnswer() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Save, Docs, "Untitled", out _);
        try
        {
            Invoke(Named<Button>(window, "CancelButton"));

            Assert.Null(window.SelectedPath);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    // ----- Focus: the keys only reach the window while something inside it has focus -----

    [Fact]
    public void FocusStartsInTheNameBox_ForASave_AndInTheList_ForAnOpen() => Ui.Run(() =>
    {
        var save = Show(Disk(), BrowserMode.Save, Docs, "x", out _);
        var open = Show(Disk(), BrowserMode.Open, Docs, null, out _);
        try
        {
            Assert.Same(save.FindName("FileNameBox"), FocusManager.GetFocusedElement(save));
            Assert.Same(open.FindName("FileList"), FocusManager.GetFocusedElement(open));
        }
        finally
        {
            save.Close();
            open.Close();
        }
    });

    [Fact]
    public void FocusIsNeverLeftEmpty_AfterNavigating() => Ui.Run(() =>
    {
        var window = Show(Disk(), BrowserMode.Open, Docs, null, out var model);
        try
        {
            Press(window, Key.Up, ModifierKeys.Alt);
            Assert.NotNull(FocusManager.GetFocusedElement(window));

            window.BeginEditAddress();
            Named<TextBox>(window, "AddressBox").Text = Desktop;
            window.CommitAddress();
            Ui.Settle();

            Assert.Equal(Desktop, model.CurrentDirectory);
            Assert.Same(window.FindName("FileList"), FocusManager.GetFocusedElement(window));
        }
        finally { window.Close(); }
    });
}

public class ThemedFileDialogsTests
{
    private const string Docs = @"C:\Users\Joe\Documents";

    private static FakeFileSystem Disk() => new FakeFileSystem()
        .AddDirectory(Docs)
        .AddFile(Docs + @"\Notes.ream")
        .AddPlace("Documents", Docs, WindowsFileSystem.QuickAccess);

    private sealed record Seen(string Title, string AcceptText, BrowserMode Mode, string Directory, string FileName);

    /// <summary>A dialog set that never shows anything: it notes what the window was and answers with <paramref name="answer"/>.</summary>
    private static ThemedFileDialogs Dialogs(List<Seen> seen, Func<FileBrowserWindow, bool?> answer) => new(Disk(), window =>
    {
        seen.Add(new Seen(
            window.Title,
            (string)((Button)window.FindName("AcceptButton")).Content,
            window.Model.Mode,
            window.Model.CurrentDirectory,
            ((TextBox)window.FindName("FileNameBox")).Text));
        return answer(window);
    });

    [Fact]
    public void PickOpenReam_IsAnOpenBrowser_InTheGivenFolder() => Ui.Run(() =>
    {
        var seen = new List<Seen>();
        var dialogs = Dialogs(seen, _ => false);

        Assert.Null(dialogs.PickOpenReam(Docs));

        var s = Assert.Single(seen);
        Assert.Equal(("Open ream", "Open", BrowserMode.Open, Docs), (s.Title, s.AcceptText, s.Mode, s.Directory));
    });

    [Fact]
    public void PickNewReam_IsASaveBrowser_WithTheSuggestedName() => Ui.Run(() =>
    {
        var seen = new List<Seen>();
        var dialogs = Dialogs(seen, _ => false);

        Assert.Null(dialogs.PickNewReam("Untitled", Docs));

        var s = Assert.Single(seen);
        Assert.Equal(("New ream", "Create", BrowserMode.Save, "Untitled"), (s.Title, s.AcceptText, s.Mode, s.FileName));
    });

    [Fact]
    public void PickSaveAs_IsASaveBrowser_WithTheSuggestedName() => Ui.Run(() =>
    {
        var seen = new List<Seen>();
        var dialogs = Dialogs(seen, _ => false);

        Assert.Null(dialogs.PickSaveAs("Copy of Notes", Docs));

        var s = Assert.Single(seen);
        Assert.Equal(("Save ream as", "Save", BrowserMode.Save, "Copy of Notes"), (s.Title, s.AcceptText, s.Mode, s.FileName));
    });

    [Fact]
    public void WhenTheUserAccepts_ThePathComesBack() => Ui.Run(() =>
    {
        // The window's own accept produces the path, exactly as a click on the button would.
        string? path = new ThemedFileDialogs(Disk(), window =>
        {
            ((TextBox)window.FindName("FileNameBox")).Text = "Fresh";
            window.Accept();
            return true;
        }).PickNewReam("Untitled", Docs);

        Assert.Equal(Docs + @"\Fresh.ream", path);
    });

    [Fact]
    public void WhenTheUserCancels_ThereIsNoPath() => Ui.Run(() =>
    {
        var dialogs = new ThemedFileDialogs(Disk(), window =>
        {
            ((TextBox)window.FindName("FileNameBox")).Text = "Fresh";
            window.Accept();
            return false;
        });

        Assert.Null(dialogs.PickNewReam("Untitled", Docs));
    });
}
