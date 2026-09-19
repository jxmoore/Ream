using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Ream.App;
using Ream.App.Services;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>File | Home | View: three tabs, each swapping the ribbon panel under them.</summary>
public class RibbonTabTests
{
    /// <summary>These tests look at the panels themselves, so the ribbon is docked (auto-hide would keep it tucked away).</summary>
    private static WindowFixture Docked(params (string? Name, int Notes)[] workspaces) =>
        new(new AppConfig { Ribbon = new RibbonConfig { AutoHide = false } }, workspaces);

    private static RadioButton Tab(WindowFixture fx, string name) => (RadioButton)fx.Window.FindName(name);

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Visibility[] Panels(WindowFixture fx) =>
    [
        ((UIElement)fx.Window.FindName("FileRibbon")).Visibility,
        ((UIElement)fx.Window.FindName("Ribbon")).Visibility,
        ((UIElement)fx.Window.FindName("ViewRibbon")).Visibility,
    ];

    [Fact]
    public void TheTopBar_HasFileHomeAndViewTabs_WithHomeSelected() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));

        Assert.Equal(["File", "Home", "View"], new[] { "FileTab", "HomeTab", "ViewTab" }.Select(n => (string)Tab(fx, n).Content));
        Assert.Equal([false, true, false], new[] { "FileTab", "HomeTab", "ViewTab" }.Select(n => Tab(fx, n).IsChecked == true));
        Assert.Equal(RibbonTab.Home, fx.Window.SelectedTab);
        Assert.Equal([Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed], Panels(fx));
    });

    [Fact]
    public void ClickingATab_ShowsItsRibbon_AndOnlyThat() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));

        Click(Tab(fx, "FileTab"));
        Assert.Equal(RibbonTab.File, fx.Window.SelectedTab);
        Assert.Equal([Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed], Panels(fx));
        Assert.Equal([true, false, false], new[] { "FileTab", "HomeTab", "ViewTab" }.Select(n => Tab(fx, n).IsChecked == true));

        Click(Tab(fx, "ViewTab"));
        Assert.Equal(RibbonTab.View, fx.Window.SelectedTab);
        Assert.Equal([Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible], Panels(fx));

        Click(Tab(fx, "HomeTab"));
        Assert.Equal([Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed], Panels(fx));
    });

    [Fact]
    public void TheOldFileMenuAndSettingsCog_AreGone() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));

        foreach (var name in new[] { "FileButton", "FileMenu", "SettingsButton", "SettingsPopup", "SettingsPanel", "WorkspacesMenuItem" })
            Assert.Null(fx.Window.FindName(name));
    });

    [Fact]
    public void TheViewRibbon_IsBoundToTheSettings() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));

        Assert.Same(fx.Window.Settings, ((FrameworkElement)fx.Window.FindName("ViewRibbon")).DataContext);
    });

    [Fact]
    public void ThePanelKeepsItsHeight_WhicheverTabIsShowing() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
        var panel = (FrameworkElement)fx.Window.FindName("RibbonPanel");
        var heights = new List<double> { panel.ActualHeight };

        foreach (var name in new[] { "FileTab", "ViewTab", "HomeTab" })
        {
            Click(Tab(fx, name));
            Ui.Settle();
            heights.Add(panel.ActualHeight);
        }

        Assert.True(heights.Distinct().Count() == 1, string.Join(", ", heights));
    });

    // ----- The File ribbon -----

    private static FileRibbonView FileRibbon(WindowFixture fx)
    {
        fx.Window.SelectTab(RibbonTab.File);
        Ui.Settle();
        return (FileRibbonView)fx.Window.FindName("FileRibbon");
    }

    private static Button ButtonNamed(FileRibbonView view, string name) => (Button)view.FindName(name);

    private static List<Button> WorkspaceButtons(FileRibbonView view) =>
        Ui.Descendants<Button>(view).Where(b => b.Name == "WorkspaceButton").ToList();

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    [Fact]
    public void TheFileRibbon_HasOpenDisabled_TheWorkspaces_AndHelpAndAbout() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
        var view = FileRibbon(fx);

        var open = ButtonNamed(view, "OpenButton");
        Assert.False(open.IsEnabled);
        Assert.Equal("Coming soon", open.ToolTip);
        Assert.True(ButtonNamed(view, "HelpButton").IsEnabled);
        Assert.True(ButtonNamed(view, "AboutButton").IsEnabled);
    });

    [Fact]
    public void TheWorkspaceButtons_ListEveryWorkspace_WithTheCurrentOneMarked() => Ui.Run(() =>
    {
        using var fx = Docked(("Work", 1), (null, 1));
        var view = FileRibbon(fx);

        var buttons = WorkspaceButtons(view);

        Assert.Equal(
            ["New workspace above", "Work", "Workspace 2", "New workspace below"],
            buttons.Select(b => ((TextBlock)b.Content).Text));
        Assert.Equal([false, true, false, false], buttons.Select(b => b.BorderThickness.Left > 1));
    });

    [Fact]
    public void PickingAWorkspace_SwitchesToIt_AndTheLabelFollows() => Ui.Run(() =>
    {
        using var fx = Docked(("Work", 1), ("Ideas", 1));
        var view = FileRibbon(fx);

        Invoke(WorkspaceButtons(view)[2]);
        Ui.Settle();

        Assert.Same(fx.App.Workspaces[2], fx.App.CurrentWorkspace);
        Assert.Equal("Ideas", ((TextBlock)fx.Window.FindName("WorkspaceLabel")).Text);
    });

    [Fact]
    public void PickingOne_LandsOnItsFirstNote() => Ui.Run(() =>
    {
        using var fx = Docked(("Work", 1), ("Ideas", 4));
        fx.App.Workspaces[2].SetFocus(3);
        var view = FileRibbon(fx);
        Assert.Equal(1, fx.App.CurrentIndex);
        Assert.Equal(3, fx.App.Workspaces[2].FocusedIndex);

        Invoke(WorkspaceButtons(view)[2]);
        Ui.Settle();

        Assert.Equal(2, fx.App.CurrentIndex);
        Assert.Equal(0, fx.App.Workspaces[2].FocusedIndex);
    });

    [Fact]
    public void TheWorkspaceList_KeepsUpWithRenamesAndNewWorkspaces() => Ui.Run(() =>
    {
        using var fx = Docked(("Work", 1));
        var view = FileRibbon(fx);
        Assert.Equal(["New workspace above", "Work", "New workspace below"], WorkspaceButtons(view).Select(b => ((TextBlock)b.Content).Text));

        fx.App.CurrentWorkspace.Name = "Renamed";
        fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[^1]);
        fx.App.NewNoteCommand.Execute(null); // a note in the last empty workspace makes another appear
        Ui.Settle();

        var labels = WorkspaceButtons(view).Select(b => ((TextBlock)b.Content).Text).ToList();
        Assert.Equal(fx.App.Workspaces.Count, labels.Count);
        Assert.Contains("Renamed", labels);
    });

    [Fact]
    public void Open_DoesNothing_EvenIfClicked() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
        var view = FileRibbon(fx);
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        Click(ButtonNamed(view, "OpenButton"));

        Assert.Empty(shown);
    });

    [Fact]
    public void Help_OpensAWindowListingTheShortcuts() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
        var view = FileRibbon(fx);
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        Click(ButtonNamed(view, "HelpButton"));

        var help = Assert.IsType<HelpWindow>(Assert.Single(shown));
        Assert.Same(fx.Window, help.Owner);

        var sections = ((IEnumerable<HelpSection>)help.Sections.ItemsSource).ToList();
        Assert.Equal(["Notes", "Workspaces", "Size and view", "Mouse", "Editing"], sections.Select(s => s.Title));
        Assert.Contains(sections.SelectMany(s => s.Entries), e => e.Gesture == "F11");
    });

    [Fact]
    public void Help_ShowsWhateverTheConfigCurrentlySays() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
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
        using var fx = Docked(("W", 1));
        var view = FileRibbon(fx);
        fx.Window.About = AboutInfo.Create("D:/notes", "C:/cfg/config.json");
        var shown = new List<Window>();
        fx.Window.ShowModal = shown.Add;

        Click(ButtonNamed(view, "AboutButton"));

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
        Assert.Equal("https://github.com/jxmoore/Ream", ((System.Windows.Documents.Hyperlink)about.FindName("RepositoryLink")).NavigateUri.AbsoluteUri.TrimEnd('/'));
        about.Close();
    });

    [Fact]
    public void ClosingAWindow_ReturnsTheKeyboardToTheEditor() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
        int requests = 0;
        fx.App.FocusEditorRequested += () => requests++;
        fx.Window.ShowModal = _ => { };

        fx.Window.OpenAbout();

        Assert.Equal(1, requests);
    });

    // ----- Scrolling -----

    [Fact]
    public void ANarrowWindow_ScrollsTheRibbonSideways_AWideOneDoesNot() => Ui.Run(() =>
    {
        using var fx = Docked(("W", 1));
        var scroll = (ScrollViewer)fx.Window.FindName("RibbonScroll");

        fx.Window.Width = 1600;
        Ui.Settle();
        Assert.Equal(0, scroll.ScrollableWidth);

        fx.Window.Width = 500;
        Ui.Settle();
        Assert.True(scroll.ScrollableWidth > 0, "a 500 px wide window can't fit the Home ribbon");
    });
}
