using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Ream.App.Services;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

public class HelpContentTests
{
    [Fact]
    public void EveryDefaultBinding_HasADescription_AndEveryDescriptionAnAction()
    {
        var defaults = AppConfig.DefaultKeybindings().Keys.OrderBy(k => k).ToList();
        var described = ActionCatalog.All.Select(a => a.Id).OrderBy(k => k).ToList();

        Assert.Equal(defaults, described);
        Assert.Equal(ActionCatalog.All.Count, ActionCatalog.All.Select(a => a.Id).Distinct().Count());
        Assert.All(ActionCatalog.All, a => Assert.False(string.IsNullOrWhiteSpace(a.Description)));
    }

    [Fact]
    public void Help_ListsEveryActionWithItsCurrentGesture()
    {
        var sections = HelpContent.Build(AppConfig.DefaultKeybindings());
        var entries = sections.SelectMany(s => s.Entries).ToList();

        Assert.Contains(entries, e => e.Description.StartsWith("New note") && e.Gesture == "Alt + N");
        Assert.Contains(entries, e => e.Description == "Make the note wider" && e.Gesture == "Alt + =");
        Assert.Contains(entries, e => e.Description == "Make the note narrower" && e.Gesture == "Alt + -");
        Assert.Contains(entries, e => e.Description == "App fullscreen, and back" && e.Gesture == "F11");
        Assert.Contains(entries, e => e.Description == "Note fullscreen, and back" && e.Gesture == "Alt + F11");
        Assert.Contains(entries, e => e.Description == "Rename the note" && e.Gesture == "F2");
        Assert.Contains(entries, e => e.Description == "Rename the workspace" && e.Gesture == "Shift + F2");
    }

    [Fact]
    public void Help_ShowsAGestureTheUserRebound()
    {
        var bindings = AppConfig.DefaultKeybindings();
        bindings["newNote"] = "Ctrl+Shift+T";

        var entries = HelpContent.Build(bindings).SelectMany(s => s.Entries).ToList();

        Assert.Contains(entries, e => e.Description.StartsWith("New note") && e.Gesture == "Ctrl + Shift + T");
        Assert.DoesNotContain(entries, e => e.Description.StartsWith("New note") && e.Gesture == "Alt + N");
    }

    [Fact]
    public void AnActionWithNoGesture_SaysUnbound()
    {
        var bindings = AppConfig.DefaultKeybindings();
        bindings.Remove("closeNote");

        var entry = HelpContent.Build(bindings).SelectMany(s => s.Entries).Single(e => e.Description == "Close the note");

        Assert.Equal("Unbound", entry.Gesture);
    }

    [Fact]
    public void AnActionWeHaveNoDescriptionFor_IsStillListed()
    {
        var bindings = AppConfig.DefaultKeybindings();
        bindings["someFutureAction"] = "Ctrl+Q";

        var sections = HelpContent.Build(bindings);

        var other = sections.Single(s => s.Title == "Other");
        Assert.Equal([new HelpEntry("someFutureAction", "Ctrl + Q")], other.Entries);
    }

    [Fact]
    public void Help_AlsoCoversTheMouseAndEditingShortcuts()
    {
        var sections = HelpContent.Build(AppConfig.DefaultKeybindings());

        Assert.Equal(["Notes", "Workspaces", "Size and view", "Mouse", "Editing"], sections.Select(s => s.Title));
        Assert.Contains(sections.Single(s => s.Title == "Mouse").Entries, e => e.Gesture == "Alt + Scroll");
        Assert.Contains(sections.Single(s => s.Title == "Editing").Entries, e => e.Gesture == "Ctrl + B");
    }

    [Theory]
    [InlineData("Alt+Right", "Alt + Right")]
    [InlineData("Alt+OemPlus", "Alt + =")]
    [InlineData("Alt+OemMinus", "Alt + -")]
    [InlineData("F11", "F11")]
    [InlineData("Shift+F2", "Shift + F2")]
    [InlineData("Ctrl+Return", "Ctrl + Enter")]
    [InlineData("Ctrl+OemComma", "Ctrl + ,")]
    [InlineData("", "Unbound")]
    [InlineData(null, "Unbound")]
    [InlineData("  ", "Unbound")]
    public void GesturesAreShownAsPeopleReadThem(string? gesture, string expected)
    {
        Assert.Equal(expected, GestureText.Pretty(gesture));
    }
}

public class AboutInfoTests
{
    [Fact]
    public void TheVersion_IsTheAssemblysWithoutBuildMetadata()
    {
        string version = AboutInfo.VersionOf(typeof(AboutInfo).Assembly);

        Assert.StartsWith("0.5.0", version);
        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public void ItNamesTheAppTheRuntimeAndTheRepository()
    {
        var info = AboutInfo.Create("D:/notes", "C:/cfg/config.json");

        Assert.Equal("Ream", info.Name);
        Assert.Contains(".NET", info.Runtime);
        Assert.False(string.IsNullOrWhiteSpace(info.System));
        Assert.Equal("D:/notes", info.DocumentsFolder);
        Assert.Equal("C:/cfg/config.json", info.ConfigFile);
        Assert.Equal("https://github.com/jxmoore/Ream", info.Repository);
    }

    [Fact]
    public void UnknownFolders_ShowADash()
    {
        var info = AboutInfo.Create();

        Assert.Equal("-", info.DocumentsFolder);
        Assert.Equal("-", info.ConfigFile);
    }
}

public class RibbonTests
{
    private static Button? Find(RibbonView ribbon, string name) => (Button?)ribbon.FindName(name);

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public void TheRibbonHasWordsFourGroups_EachLabelled() => Ui.Run(() =>
    {
        var ribbon = new RibbonView();
        using var host = new Host(ribbon);

        var labels = Ui.Descendants<TextBlock>(ribbon)
            .Where(t => t.Style is not null && t.Text is "Clipboard" or "Font" or "Paragraph" or "Styles")
            .Select(t => t.Text)
            .ToList();

        Assert.Equal(["Clipboard", "Font", "Paragraph", "Styles"], labels);
    });

    [Fact]
    public void EveryGroupHoldsItsControls() => Ui.Run(() =>
    {
        var ribbon = new RibbonView();
        using var host = new Host(ribbon);

        StackPanel Group(string name) => (StackPanel)ribbon.FindName(name);

        Assert.All(new[] { "PasteButton", "CutButton", "CopyButton" }, n => Assert.True(Group("ClipboardGroup").IsAncestorOf((DependencyObject)ribbon.FindName(n))));
        Assert.All(new[] { "FontBox", "SizeBox", "BoldButton", "ItalicButton", "UnderlineButton", "StrikeButton", "TextColorButton", "HighlightButton" },
            n => Assert.True(Group("FontGroup").IsAncestorOf((DependencyObject)ribbon.FindName(n))));
        Assert.All(new[] { "BulletsButton", "NumbersButton", "AlignLeftButton", "AlignCenterButton", "AlignRightButton", "AlignJustifyButton" },
            n => Assert.True(Group("ParagraphGroup").IsAncestorOf((DependencyObject)ribbon.FindName(n))));
        Assert.All(new[] { "StyleNormalButton", "StyleHeading1Button", "StyleHeading2Button", "StyleHeading3Button" },
            n => Assert.True(Group("StylesGroup").IsAncestorOf((DependencyObject)ribbon.FindName(n))));
    });

    [Fact]
    public void ItStartsDisabled_UntilAnEditorIsAttached() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("<ReamNote schemaVersion=\"1\"><Doc><P><R>hi</R></P></Doc></ReamNote>");

        Assert.True(((Panel)fx.Toolbar.FindName("Bar")).IsEnabled);

        var loose = new RibbonView();
        Assert.False(((Panel)loose.FindName("Bar")).IsEnabled);
    });

    [Fact]
    public void TheStyleTiles_ApplyHeadings_AndShowWhichOneIsInUse() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("<ReamNote schemaVersion=\"1\"><Doc><P><R>hello world</R></P></Doc></ReamNote>");
        fx.Editor.SelectAll();

        Click(fx.Toolbar.StyleHeading2Button);
        Ui.Settle();

        Assert.Contains("size=\"22\"", fx.Saved());
        Assert.True(fx.Toolbar.StyleHeading2Button.IsChecked);
        Assert.False(fx.Toolbar.StyleNormalButton.IsChecked);
        Assert.False(fx.Toolbar.StyleHeading1Button.IsChecked);

        Click(fx.Toolbar.StyleNormalButton);
        Ui.Settle();

        Assert.DoesNotContain("size=", fx.Saved());
        Assert.True(fx.Toolbar.StyleNormalButton.IsChecked);
        Assert.False(fx.Toolbar.StyleHeading2Button.IsChecked);
    });

    [Fact]
    public void TheClipboardButtons_CutCopyAndPasteTheSelection() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("<ReamNote schemaVersion=\"1\"><Doc><P><R>alpha beta</R></P></Doc></ReamNote>");
        var editor = fx.Editor;
        var executed = new List<string>();
        editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => { executed.Add("cut"); e.Handled = true; }, (_, e) => e.CanExecute = true));
        editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { executed.Add("copy"); e.Handled = true; }, (_, e) => e.CanExecute = true));
        editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { executed.Add("paste"); e.Handled = true; }, (_, e) => e.CanExecute = true));

        Click(fx.Toolbar.PasteButton);
        Click(fx.Toolbar.CutButton);
        Click(fx.Toolbar.CopyButton);

        Assert.Equal(["paste", "cut", "copy"], executed);
    });

    [Fact]
    public void TheRibbonButtons_NeverTakeFocusFromTheEditor() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("<ReamNote schemaVersion=\"1\"><Doc><P><R>hi</R></P></Doc></ReamNote>");

        var buttons = Ui.Descendants<ButtonBase>(fx.Toolbar).ToList();

        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.False(b.Focusable, $"{b.Name} takes focus"));
    });

    /// <summary>Hosts a control in an off-screen window for the length of a test.</summary>
    private sealed class Host : IDisposable
    {
        private readonly Window _window;

        public Host(FrameworkElement content) => _window = Ui.Show(content, 1100, 200);

        public void Dispose() => _window.Close();
    }
}

public class FileMenuTests
{
    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [Fact]
    public void TheTopBarHasAFileButtonAndAHomeTab() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        var file = (Button)fx.Window.FindName("FileButton");
        var home = (Border)fx.Window.FindName("HomeTab");

        Assert.Equal("File", file.Content);
        Assert.Contains(Ui.Descendants<TextBlock>(home), t => t.Text == "Home");
        Assert.NotNull(fx.Window.FindName("Ribbon"));
    });

    [Fact]
    public void TheMenuHasOpenDisabled_ThenWorkspaces_ThenHelpAndAbout() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var menu = (ContextMenu)fx.Window.FindName("FileMenu");

        var items = menu.Items.OfType<MenuItem>().ToList();

        Assert.Equal(["Open Ream…", "Workspaces", "Help", "About"], items.Select(i => (string)i.Header));
        Assert.False(items[0].IsEnabled);
        Assert.Equal("Coming soon", items[0].ToolTip);
        Assert.True(items[1].IsEnabled);
        Assert.True(items[2].IsEnabled);
        Assert.True(items[3].IsEnabled);
        Assert.Single(menu.Items.OfType<Separator>());
    });

    private static List<MenuItem> WorkspaceItems(WindowFixture fx) =>
        ((MenuItem)fx.Window.FindName("WorkspacesMenuItem")).Items.OfType<MenuItem>().ToList();

    [Fact]
    public void TheWorkspacesSubmenu_ListsEveryWorkspace_WithTheCurrentOneChecked() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1), (null, 1));

        var items = WorkspaceItems(fx);

        Assert.Equal(
            ["New workspace above", "Work", "Workspace 2", "New workspace below"],
            items.Select(i => (string)i.Header));
        Assert.Equal([false, true, false, false], items.Select(i => i.IsChecked));
    });

    [Fact]
    public void PickingAWorkspaceFromTheMenu_SwitchesToIt_AndTheTitleFollows() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1), ("Ideas", 1));

        Click(WorkspaceItems(fx)[2]);
        Ui.Settle();

        Assert.Same(fx.App.Workspaces[2], fx.App.CurrentWorkspace);
        Assert.Equal("Ream - Ideas", fx.Window.Title);
    });

    [Fact]
    public void PickingOneFromTheMenu_LandsOnItsFirstNote() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1), ("Ideas", 4));
        fx.App.Workspaces[2].SetFocus(3);

        Click(WorkspaceItems(fx)[2]);

        Assert.Equal(0, fx.App.Workspaces[2].FocusedIndex);
    });

    [Fact]
    public void TheSubmenu_IsRebuiltToMatchTheWorkspacesAsTheyAreNow() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));
        Assert.Equal(["New workspace above", "Work", "New workspace below"], WorkspaceItems(fx).Select(i => (string)i.Header));

        fx.App.CurrentWorkspace.Name = "Renamed";
        fx.App.NewNoteCommand.Execute(null); // a note in the last empty workspace makes another appear
        fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[^1]);
        fx.App.NewNoteCommand.Execute(null);
        fx.Window.RebuildWorkspaceMenu();

        var items = WorkspaceItems(fx);
        Assert.Equal(fx.App.Workspaces.Count, items.Count);
        Assert.Contains("Renamed", items.Select(i => (string)i.Header));
        Assert.Equal(1, items.Count(i => i.IsChecked));
    });

    [Fact]
    public void TheSubmenuTemplate_CanShowAChildPopup_ACheckAndAnArrow() => Ui.Run(() =>
    {
        var parent = new MenuItem { Header = "Parent", IsChecked = true };
        parent.Items.Add(new MenuItem { Header = "Child" });
        var host = new StackPanel();
        host.Children.Add(parent);
        using var window = new Holder(Ui.Show(host, 300, 120));

        parent.ApplyTemplate();

        Assert.NotNull(parent.Template.FindName("PART_Popup", parent));
        Assert.Equal(Visibility.Visible, ((UIElement)parent.Template.FindName("Arrow", parent)).Visibility);
        Assert.Equal(Visibility.Visible, ((UIElement)parent.Template.FindName("Check", parent)).Visibility);

        var plain = new MenuItem { Header = "Plain" };
        host.Children.Add(plain);
        Ui.Settle();
        plain.ApplyTemplate();
        Assert.Equal(Visibility.Collapsed, ((UIElement)plain.Template.FindName("Arrow", plain)).Visibility);
        Assert.Equal(Visibility.Collapsed, ((UIElement)plain.Template.FindName("Check", plain)).Visibility);
    });

    private sealed class Holder(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    [Fact]
    public void Open_DoesNothing_EvenIfClicked() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        Click((MenuItem)fx.Window.FindName("OpenMenuItem"));

        Assert.Empty(shown);
    });

    [Fact]
    public void Help_OpensAWindowListingTheShortcuts() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        Click((MenuItem)fx.Window.FindName("HelpMenuItem"));

        var help = Assert.IsType<HelpWindow>(Assert.Single(shown));
        Assert.Same(fx.Window, help.Owner);

        var sections = ((IEnumerable<HelpSection>)help.Sections.ItemsSource).ToList();
        Assert.Equal(["Notes", "Workspaces", "Size and view", "Mouse", "Editing"], sections.Select(s => s.Title));
        Assert.Contains(sections.SelectMany(s => s.Entries), e => e.Gesture == "F11");
    });

    [Fact]
    public void Help_ShowsWhateverTheConfigCurrentlySays() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var config = new AppConfig();
        config.Keybindings["newNote"] = "Ctrl+T";
        fx.App.Config = config;
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        fx.Window.OpenHelp();

        var help = (HelpWindow)shown.Single();
        var entries = ((IEnumerable<HelpSection>)help.Sections.ItemsSource).SelectMany(s => s.Entries).ToList();
        Assert.Contains(entries, e => e.Description.StartsWith("New note") && e.Gesture == "Ctrl + T");
    });

    [Fact]
    public void About_OpensAWindowWithTheVersionAndTheFolders() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        fx.Window.About = AboutInfo.Create("D:/notes", "C:/cfg/config.json");
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        Click((MenuItem)fx.Window.FindName("AboutMenuItem"));

        var about = Assert.IsType<AboutWindow>(Assert.Single(shown));
        about.WindowStartupLocation = WindowStartupLocation.Manual;
        about.Left = -32000;
        about.Top = -32000;
        about.ShowActivated = false;
        about.Show();
        Ui.Settle();
        Assert.Same(fx.Window, about.Owner);
        Assert.Equal("Ream", ((TextBlock)about.FindName("NameText")).Text);
        Assert.StartsWith("Version 0.5.0", ((TextBlock)about.FindName("VersionText")).Text);
        Assert.Equal("D:/notes", ((TextBlock)about.FindName("FolderText")).Text);
        Assert.Equal("C:/cfg/config.json", ((TextBlock)about.FindName("ConfigText")).Text);
        Assert.Equal("https://github.com/jxmoore/Ream", ((Hyperlink)about.FindName("RepositoryLink")).NavigateUri.AbsoluteUri.TrimEnd('/'));
        about.Close();
    });

    [Fact]
    public void ClosingAWindow_ReturnsTheKeyboardToTheEditor() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        int requests = 0;
        fx.App.FocusEditorRequested += () => requests++;
        fx.Window.ShowModal = _ => { };

        fx.Window.OpenAbout();

        Assert.Equal(1, requests);
    });
}

public class MenuStyleTests
{
    [Fact]
    public void TheMenuUsesTheThemeColors_NotTheSystemOnes() => Ui.Run(() =>
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "One" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Two", IsEnabled = false });
        var holder = new Grid { ContextMenu = menu };
        using var window = new WindowKeeper(Ui.Show(holder, 300, 200));

        menu.ApplyTemplate();

        Assert.NotNull(menu.Template);
        var border = Ui.Descendants<Border>(menu).FirstOrDefault();
        Assert.True(border is null || border.Background is not null);
        Assert.Equal(Themes.Brush("ControlTextBrush"), ((System.Windows.Media.SolidColorBrush)menu.Foreground).Color);
    });

    private sealed class WindowKeeper(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }
}
