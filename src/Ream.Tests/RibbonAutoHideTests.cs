using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Ream.App;
using Ream.App.Views;
using Ream.Core.Layout;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class RibbonVisibilityTests
{
    [Fact]
    public void ItStartsTuckedAway_WhenAutoHideIsOn()
    {
        var state = new RibbonVisibility();

        Assert.True(state.AutoHide);
        Assert.False(state.WantsOpen);
    }

    [Fact]
    public void ThePointerOverTheTabsOrPanel_ShowsIt_AndLeavingLetsItGo()
    {
        var state = new RibbonVisibility { PointerInside = true };
        Assert.True(state.WantsOpen);

        state.PointerInside = false;
        Assert.False(state.WantsOpen);
    }

    [Fact]
    public void AnOpenMenu_KeepsItUp_EvenWithThePointerAway()
    {
        var state = new RibbonVisibility { PointerInside = true, MenuOpen = true };

        state.PointerInside = false;
        Assert.True(state.WantsOpen);

        state.MenuOpen = false;
        Assert.False(state.WantsOpen);
    }

    [Fact]
    public void ClickingATabThatIsNotShowing_PinsItOpen()
    {
        var state = new RibbonVisibility();

        state.TabClicked(wasSelected: false);

        Assert.True(state.Pinned);
        Assert.True(state.WantsOpen);
    }

    [Fact]
    public void ClickingTheShowingTabAgain_TogglesThePin()
    {
        var state = new RibbonVisibility();
        state.TabClicked(wasSelected: false);

        state.TabClicked(wasSelected: true);
        Assert.False(state.Pinned);

        state.TabClicked(wasSelected: true);
        Assert.True(state.Pinned);
    }

    [Fact]
    public void WithAutoHideOff_ItIsAlwaysUp_AndThereIsNothingToPin()
    {
        var state = new RibbonVisibility { AutoHide = false };

        Assert.True(state.WantsOpen);
        state.TabClicked(wasSelected: false);
        Assert.False(state.Pinned);
    }

    [Fact]
    public void TurningAutoHideOff_DropsThePin_SoTurningItBackOnStartsTuckedAway()
    {
        var state = new RibbonVisibility();
        state.TabClicked(wasSelected: false);

        state.AutoHide = false;
        state.AutoHide = true;

        Assert.False(state.Pinned);
        Assert.False(state.WantsOpen);
    }
}

public class RibbonConfigTests
{
    [Fact]
    public void AutoHide_IsOnByDefault()
    {
        Assert.True(new AppConfig().Ribbon.AutoHide);
    }

    [Fact]
    public void AFreshConfigFile_ShowsTheRibbonSetting()
    {
        using var dir = new TempDir();
        string path = dir.Combine("fresh", "config.json");
        new AppConfigStore(path).Load("docs");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.True((bool?)root["ribbon"]!["autoHide"]);
    }

    [Fact]
    public void ItIsReadFromTheFile_AndSurvivesWith()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, """{ "ribbon": { "autoHide": false } }""");

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out _));

        Assert.False(config.Ribbon.AutoHide);
        Assert.False(config.With(theme: "nord").Ribbon.AutoHide);
    }
}

public class RibbonAutoHideTests
{
    private static AppConfig Config(bool autoHide) => new()
    {
        Ribbon = new RibbonConfig { AutoHide = autoHide },
        Animations = new AnimationConfig { Enabled = false },
    };

    private static WindowFixture Fixture(bool autoHide, params (string? Name, int Notes)[] workspaces) =>
        new(Config(autoHide), workspaces.Length == 0 ? [("W", 2)] : workspaces);

    private static UIElement Panel(WindowFixture fx) => (UIElement)fx.Window.FindName("RibbonPanel");

    private static UIElement TabRow(WindowFixture fx) => (UIElement)fx.Window.FindName("TabRow");

    private static void Mouse(UIElement target, bool enter) =>
        target.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = enter ? System.Windows.Input.Mouse.MouseEnterEvent : System.Windows.Input.Mouse.MouseLeaveEvent,
        });

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    // ----- Where the panel lives -----

    [Fact]
    public void ByDefault_ThePanelIsTuckedAway_OverTheNotes_AndTheTabsAreStillThere() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));

        Assert.Equal(3, Grid.GetRow((UIElement)fx.Window.FindName("RibbonPanel")));
        Assert.False(fx.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Collapsed, Panel(fx).Visibility);
        Assert.Equal(Visibility.Visible, TabRow(fx).Visibility);
        Assert.True(((FrameworkElement)fx.Window.FindName("HomeTab")).IsVisible);
    });

    [Fact]
    public void ShowingThePanel_NeverMovesTheNotes() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var canvas = (FrameworkElement)fx.Window.FindName("CanvasArea");
        double before = canvas.ActualHeight;
        double top = canvas.TranslatePoint(new Point(0, 0), fx.Window).Y;

        Mouse(TabRow(fx), enter: true);
        Ui.Settle();

        Assert.True(fx.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Visible, Panel(fx).Visibility);
        Assert.Equal(before, canvas.ActualHeight);
        Assert.Equal(top, canvas.TranslatePoint(new Point(0, 0), fx.Window).Y);
    });

    [Fact]
    public void WithAutoHideOff_ThePanelIsDocked_AboveTheNotes() => Ui.Run(() =>
    {
        using var docked = Fixture(autoHide: false);
        using var hidden = Fixture(autoHide: true);

        Assert.Equal(2, Grid.GetRow(Panel(docked) as FrameworkElement));
        Assert.True(docked.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Visible, Panel(docked).Visibility);

        var dockedCanvas = (FrameworkElement)docked.Window.FindName("CanvasArea");
        var hiddenCanvas = (FrameworkElement)hidden.Window.FindName("CanvasArea");
        Assert.True(hiddenCanvas.ActualHeight - dockedCanvas.ActualHeight >= 90, "docking the ribbon takes room from the notes");
    });

    // ----- Hover -----

    [Fact]
    public void HoveringTheTabRow_ShowsThePanel_AndLeavingStartsTheCountdown() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);

        Mouse(TabRow(fx), enter: true);
        Assert.True(fx.Window.IsRibbonOpen);
        Assert.False(fx.Window.RibbonHidePending);

        Mouse(TabRow(fx), enter: false);
        Assert.True(fx.Window.IsRibbonOpen);
        Assert.True(fx.Window.RibbonHidePending);

        fx.Window.CompleteRibbonHide();
        Assert.False(fx.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Collapsed, Panel(fx).Visibility);
    });

    [Fact]
    public void MovingFromTheTabsOntoThePanel_DoesNotTuckItAway() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        Mouse(TabRow(fx), enter: true);

        Mouse(TabRow(fx), enter: false);
        Mouse(Panel(fx), enter: true);
        fx.Window.CompleteRibbonHide();

        Assert.True(fx.Window.IsRibbonOpen);
        Assert.False(fx.Window.RibbonHidePending);
    });

    [Fact]
    public void LeavingThePanel_TucksItAwayAfterTheDelay() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        Mouse(TabRow(fx), enter: true);
        Mouse(Panel(fx), enter: true);
        Mouse(TabRow(fx), enter: false);

        Mouse(Panel(fx), enter: false);
        Assert.True(fx.Window.RibbonHidePending);
        fx.Window.CompleteRibbonHide();

        Assert.False(fx.Window.IsRibbonOpen);
    });

    [Fact]
    public void ComingBackDuringTheCountdown_CancelsIt() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        Mouse(TabRow(fx), enter: true);
        Mouse(TabRow(fx), enter: false);
        Assert.True(fx.Window.RibbonHidePending);

        Mouse(TabRow(fx), enter: true);

        Assert.False(fx.Window.RibbonHidePending);
        fx.Window.CompleteRibbonHide();
        Assert.True(fx.Window.IsRibbonOpen);
    });

    // ----- Pinning -----

    [Fact]
    public void ClickingATab_PinsThePanelOpen_UntilThatTabIsClickedAgain() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var view = (RadioButton)fx.Window.FindName("ViewTab");

        Click(view);
        Assert.True(fx.Window.IsRibbonOpen);
        Assert.True(fx.Window.RibbonState.Pinned);
        Assert.Equal(RibbonTab.View, fx.Window.SelectedTab);
        Assert.Equal(Visibility.Visible, ((UIElement)fx.Window.FindName("ViewRibbon")).Visibility);

        Mouse(TabRow(fx), enter: false); // the pointer is nowhere near, and it stays
        Assert.False(fx.Window.RibbonHidePending);

        Click(view);
        Assert.False(fx.Window.RibbonState.Pinned);
        Mouse(TabRow(fx), enter: false);
        fx.Window.CompleteRibbonHide();
        Assert.False(fx.Window.IsRibbonOpen);
    });

    [Fact]
    public void ChangingTabsBySelectTab_DoesNotPin() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);

        fx.Window.SelectTab(RibbonTab.View);

        Assert.False(fx.Window.RibbonState.Pinned);
        Assert.False(fx.Window.IsRibbonOpen);
    });

    // ----- Menus -----

    [Fact]
    public void AnOpenDropDown_KeepsThePanelUp_UntilItCloses() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var home = (RibbonView)fx.Window.FindName("Ribbon");
        var fontBox = (ComboBox)home.FindName("FontBox");

        Mouse(TabRow(fx), enter: true);
        Ui.Settle(); // a drop-down only opens once it has been laid out
        fontBox.IsDropDownOpen = true;
        Ui.Settle();
        Assert.True(home.IsMenuOpen);
        Mouse(TabRow(fx), enter: false);
        Mouse(Panel(fx), enter: false);
        fx.Window.CompleteRibbonHide();

        Assert.True(fx.Window.IsRibbonOpen);

        fontBox.IsDropDownOpen = false;
        Ui.Settle(); // the countdown may even finish in here; finishing it again changes nothing
        fx.Window.CompleteRibbonHide();
        Assert.False(fx.Window.IsRibbonOpen);
    });

    [Fact]
    public void AnOpenColorMenu_KeepsThePanelUp() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var home = (RibbonView)fx.Window.FindName("Ribbon");
        Mouse(TabRow(fx), enter: true);
        Ui.Settle();

        Click((Button)home.FindName("TextColorButton"));
        Ui.Settle();
        Assert.True(home.IsMenuOpen);
        Mouse(TabRow(fx), enter: false);
        fx.Window.CompleteRibbonHide();

        Assert.True(fx.Window.IsRibbonOpen);
    });

    // ----- Live config -----

    [Fact]
    public void FlippingAutoHideInTheConfig_DocksOrTucksThePanelAtOnce() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        Assert.False(fx.Window.IsRibbonOpen);

        fx.App.Config = Config(autoHide: false);
        Ui.Settle();
        Assert.True(fx.Window.IsRibbonOpen);
        Assert.Equal(2, Grid.GetRow((FrameworkElement)Panel(fx)));

        fx.App.Config = Config(autoHide: true);
        Ui.Settle();
        Assert.False(fx.Window.IsRibbonOpen);
        Assert.Equal(3, Grid.GetRow((FrameworkElement)Panel(fx)));
    });

    // ----- The Size group -----

    private static Button SizeButton(WindowFixture fx, string name) =>
        (Button)((RibbonView)fx.Window.FindName("Ribbon")).FindName(name);

    [Fact]
    public void TheSizeButtons_WorkWithNoEditorFocused_WhileTheEditingControlsWait() => Ui.Run(() =>
    {
        var view = new RibbonView();
        var window = Ui.Show(view, 1800, 120);
        try
        {
            foreach (var name in new[] { "ResetNoteSizeButton", "ResetWorkspaceSizesButton", "ResetAllSizesButton" })
                Assert.True(((Button)view.FindName(name)).IsEnabled, name);
            Assert.False(((UIElement)view.FindName("Bar")).IsEnabled);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TheThreeSizeButtons_ResetTheNote_TheWorkspace_AndEverything() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false, ("A", 2), ("B", 2));
        var all = fx.App.Workspaces.SelectMany(w => w.Notes).ToList();
        void Widen() { foreach (var n in all) n.WidthFraction = 0.8; }
        var focused = fx.App.CurrentWorkspace.FocusedNote!;

        Widen();
        Invoke(SizeButton(fx, "ResetNoteSizeButton"));
        Ui.Settle();
        Assert.Equal(WidthPresets.Default, focused.WidthFraction);
        Assert.Equal(3, all.Count(n => n.WidthFraction == 0.8));

        Widen();
        Invoke(SizeButton(fx, "ResetWorkspaceSizesButton"));
        Ui.Settle();
        Assert.All(fx.App.CurrentWorkspace.Notes, n => Assert.Equal(WidthPresets.Default, n.WidthFraction));
        Assert.Equal(2, all.Count(n => n.WidthFraction == 0.8));

        Widen();
        Invoke(SizeButton(fx, "ResetAllSizesButton"));
        Ui.Settle();
        Assert.All(all, n => Assert.Equal(WidthPresets.Default, n.WidthFraction));
    });

    // ----- One row per group, and a ribbon that scrolls -----

    [Fact]
    public void EachGroupsControls_SitInOneRow() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false);
        var home = (RibbonView)fx.Window.FindName("Ribbon");
        double CenterY(string name)
        {
            var element = (FrameworkElement)home.FindName(name);
            return element.TranslatePoint(new Point(0, element.ActualHeight / 2), fx.Window).Y;
        }

        var font = new[] { "FontBox", "SizeBox", "BoldButton", "ItalicButton", "UnderlineButton", "StrikeButton", "TextColorButton", "HighlightButton" }.Select(CenterY).ToList();
        var paragraph = new[] { "BulletsButton", "NumbersButton", "AlignLeftButton", "AlignCenterButton", "AlignRightButton", "AlignJustifyButton" }.Select(CenterY).ToList();
        var clipboard = new[] { "PasteButton", "CutButton", "CopyButton" }.Select(CenterY).ToList();

        Assert.All(font, y => Assert.InRange(y, font[0] - 2, font[0] + 2));
        Assert.All(paragraph, y => Assert.InRange(y, paragraph[0] - 2, paragraph[0] + 2));
        Assert.All(clipboard, y => Assert.InRange(y, clipboard[0] - 2, clipboard[0] + 2));
    });

    [Fact]
    public void ThePlainWheelOverTheRibbon_ScrollsItSideways() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false);
        fx.Window.Width = 500;
        Ui.Settle();
        var scroll = (ScrollViewer)fx.Window.FindName("RibbonScroll");
        Assert.True(scroll.ScrollableWidth > 0);

        scroll.RaiseEvent(new MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        });
        Ui.Settle();

        Assert.True(scroll.HorizontalOffset > 0);
    });
}
