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
    public void ClickingATabThatIsNotShowing_HoldsItOpen_WithoutPinningIt()
    {
        var state = new RibbonVisibility();

        state.TabClicked(wasSelected: false);

        Assert.True(state.Engaged);
        Assert.False(state.Pinned);
        Assert.True(state.WantsOpen);
    }

    [Fact]
    public void ClickingTheShowingTabAgain_TogglesIt()
    {
        var state = new RibbonVisibility();
        state.TabClicked(wasSelected: false);

        state.TabClicked(wasSelected: true);
        Assert.False(state.Engaged);

        state.TabClicked(wasSelected: true);
        Assert.True(state.Engaged);
    }

    [Fact]
    public void DismissingForgetsTheTabClickAndThePointer_ButNotThePin()
    {
        var state = new RibbonVisibility { PointerInside = true };
        state.TabClicked(wasSelected: false);
        state.TogglePin();

        state.Dismiss();

        Assert.False(state.Engaged);
        Assert.False(state.PointerInside);
        Assert.True(state.Pinned);
        Assert.True(state.WantsOpen);

        state.TogglePin();
        Assert.False(state.WantsOpen);
    }

    [Fact]
    public void WithAutoHideOff_ItIsAlwaysUp_AndThereIsNothingToPin()
    {
        var state = new RibbonVisibility { AutoHide = false };

        Assert.True(state.WantsOpen);
        state.TabClicked(wasSelected: false);
        state.TogglePin();
        Assert.False(state.Engaged);
        Assert.False(state.Pinned);
    }

    [Fact]
    public void TurningAutoHideOff_DropsThePinAndTheTabClick_SoTurningItBackOnStartsTuckedAway()
    {
        var state = new RibbonVisibility();
        state.TabClicked(wasSelected: false);
        state.TogglePin();

        state.AutoHide = false;
        state.AutoHide = true;

        Assert.False(state.Pinned);
        Assert.False(state.Engaged);
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
    public void ByDefault_ThePanelIsTuckedAway_AndTheTabsAreStillThere() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));

        Assert.False(fx.Window.IsRibbonOpen);
        Assert.Equal(0, ((FrameworkElement)Panel(fx)).ActualHeight);
        Assert.Equal(Visibility.Collapsed, Panel(fx).Visibility);
        Assert.Equal(Visibility.Visible, TabRow(fx).Visibility);
        Assert.True(((FrameworkElement)fx.Window.FindName("HomeTab")).IsVisible);
    });

    [Fact]
    public void ShowingThePanel_PushesTheNotesDown_AndPuttingItAwayBringsThemBack() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var canvas = (FrameworkElement)fx.Window.FindName("CanvasArea");
        double height = canvas.ActualHeight;
        double top = canvas.TranslatePoint(new Point(0, 0), fx.Window).Y;

        Mouse(TabRow(fx), enter: true);
        Ui.Settle();

        Assert.True(fx.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Visible, Panel(fx).Visibility);
        double panel = ((FrameworkElement)Panel(fx)).ActualHeight;
        Assert.True(panel >= 85, $"the panel is {panel} px tall");
        Assert.Equal(height - panel, canvas.ActualHeight, 1);
        Assert.Equal(top + panel, canvas.TranslatePoint(new Point(0, 0), fx.Window).Y, 1);

        Mouse(TabRow(fx), enter: false);
        fx.Window.CompleteRibbonHide();
        Ui.Settle();

        Assert.Equal(height, canvas.ActualHeight, 1);
        Assert.Equal(top, canvas.TranslatePoint(new Point(0, 0), fx.Window).Y, 1);
    });

    [Fact]
    public void WithAnimationsOn_ThePanelEasesOpen_AndEndsAtItsFullHeight() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2)); // the default config: animations on
        var panel = (FrameworkElement)Panel(fx);

        Mouse(TabRow(fx), enter: true);
        Assert.True(panel.HasAnimatedProperties, "the height is animating");

        // The animation runs on the wall clock, and an off-screen layered window renders slowly: give it time.
        for (int i = 0; i < 100 && panel.ActualHeight < 89.5; i++)
        {
            Ui.Settle();
            Thread.Sleep(30);
        }

        Assert.InRange(panel.ActualHeight, 89.5, 90.5);
    });

    [Fact]
    public void WithAutoHideOff_ThePanelIsAlwaysThere_AboveTheNotes() => Ui.Run(() =>
    {
        using var docked = Fixture(autoHide: false);
        using var hidden = Fixture(autoHide: true);

        Assert.Equal(2, Grid.GetRow(Panel(docked) as FrameworkElement));
        Assert.True(docked.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Visible, Panel(docked).Visibility);

        var dockedCanvas = (FrameworkElement)docked.Window.FindName("CanvasArea");
        var hiddenCanvas = (FrameworkElement)hidden.Window.FindName("CanvasArea");
        Assert.True(hiddenCanvas.ActualHeight - dockedCanvas.ActualHeight >= 90, "a shown ribbon takes room from the notes");
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

    // ----- Getting it out of the way -----

    private static void MouseDown(UIElement target) =>
        target.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent,
        });

    private static void EscapeKey(WindowFixture fx) =>
        fx.Window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fx.Window)!, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    [Fact]
    public void ClickingATab_HoldsThePanelOpen_UntilYouClickElsewhere() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var view = (RadioButton)fx.Window.FindName("ViewTab");

        Click(view);
        Assert.True(fx.Window.IsRibbonOpen);
        Assert.True(fx.Window.RibbonState.Engaged);
        Assert.False(fx.Window.RibbonState.Pinned);
        Assert.Equal(RibbonTab.View, fx.Window.SelectedTab);
        Assert.Equal(Visibility.Visible, ((UIElement)fx.Window.FindName("ViewRibbon")).Visibility);

        Mouse(TabRow(fx), enter: false); // the pointer is nowhere near, and it stays
        Assert.False(fx.Window.RibbonHidePending);

        MouseDown(fx.Canvas);

        Assert.False(fx.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Collapsed, Panel(fx).Visibility);
    });

    [Fact]
    public void ClickingInsideTheRibbon_DoesNotPutItAway() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        Mouse(TabRow(fx), enter: true);

        MouseDown((UIElement)fx.Window.FindName("HomeTab"));
        MouseDown(((RibbonView)fx.Window.FindName("Ribbon")).FindName("BoldButton") as UIElement ?? Panel(fx));

        Assert.True(fx.Window.IsRibbonOpen);
    });

    [Fact]
    public void Escape_PutsAnOpenRibbonAway_EvenWithThePointerOverIt() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        Mouse(TabRow(fx), enter: true);
        Click((RadioButton)fx.Window.FindName("FileTab"));
        Assert.True(fx.Window.IsRibbonOpen);

        EscapeKey(fx);

        Assert.False(fx.Window.IsRibbonOpen);
        Assert.False(fx.Window.RibbonState.Engaged);
        Assert.False(fx.Window.RibbonState.PointerInside);
    });

    [Fact]
    public void ClickingTheShowingTabAgain_LetsTheRibbonGoWhenThePointerLeaves() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var home = (RadioButton)fx.Window.FindName("HomeTab");
        Mouse(TabRow(fx), enter: true);

        Click(home); // engage
        Click(home); // and let go
        Mouse(TabRow(fx), enter: false);
        fx.Window.CompleteRibbonHide();

        Assert.False(fx.Window.IsRibbonOpen);
    });

    [Fact]
    public void ThePinButton_KeepsTheRibbonOpen_UntilItIsClickedAgain() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var pin = (ToggleButton)fx.Window.FindName("PinButton");
        Assert.Equal(Visibility.Visible, pin.Visibility);

        Click(pin);
        Assert.True(fx.Window.RibbonState.Pinned);
        Assert.True(pin.IsChecked);
        Assert.True(fx.Window.IsRibbonOpen);

        MouseDown(fx.Canvas); // pinned: a click elsewhere leaves it alone
        Mouse(TabRow(fx), enter: false);
        Assert.True(fx.Window.IsRibbonOpen);

        Click(pin);
        Assert.False(fx.Window.RibbonState.Pinned);
        fx.Window.CompleteRibbonHide();
        Assert.False(fx.Window.IsRibbonOpen);
    });

    [Fact]
    public void ThePinButton_LivesInTheRibbonPanel_NotInTheTabRow() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);
        var pin = (DependencyObject)fx.Window.FindName("PinButton");

        bool Inside(DependencyObject child, DependencyObject ancestor)
        {
            for (var n = child; n is not null; n = (n is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(n) : null) ?? LogicalTreeHelper.GetParent(n))
                if (ReferenceEquals(n, ancestor)) return true;
            return false;
        }

        Assert.True(Inside(pin, (DependencyObject)Panel(fx)));
        Assert.False(Inside(pin, (DependencyObject)TabRow(fx)));
    });

    [Fact]
    public void ThePinButton_IsOnlyThereWhenAutoHideIs() => Ui.Run(() =>
    {
        using var docked = Fixture(autoHide: false);

        Assert.Equal(Visibility.Collapsed, ((UIElement)docked.Window.FindName("PinButton")).Visibility);
    });

    [Fact]
    public void ChangingTabsBySelectTab_DoesNotHoldTheRibbonOpen() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: true);

        fx.Window.SelectTab(RibbonTab.View);

        Assert.False(fx.Window.RibbonState.Engaged);
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
        Assert.Equal(Visibility.Visible, Panel(fx).Visibility);

        fx.App.Config = Config(autoHide: true);
        Ui.Settle();
        Assert.False(fx.Window.IsRibbonOpen);
        Assert.Equal(Visibility.Collapsed, Panel(fx).Visibility);
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

    // ----- Stacked groups, and a ribbon that scrolls without a scrollbar -----

    private static double CenterY(WindowFixture fx, string name)
    {
        var home = (RibbonView)fx.Window.FindName("Ribbon");
        var element = (FrameworkElement)home.FindName(name);
        return element.TranslatePoint(new Point(0, element.ActualHeight / 2), fx.Window).Y;
    }

    private static void SameRow(WindowFixture fx, params string[] names)
    {
        var ys = names.Select(n => CenterY(fx, n)).ToList();
        Assert.All(ys, y => Assert.InRange(y, ys[0] - 2, ys[0] + 2));
    }

    [Fact]
    public void TheFontAndParagraphGroups_StackInTwoRows() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false);

        SameRow(fx, "FontBox", "SizeBox");
        SameRow(fx, "BoldButton", "ItalicButton", "UnderlineButton", "StrikeButton", "TextColorButton", "HighlightButton");
        Assert.True(CenterY(fx, "BoldButton") - CenterY(fx, "FontBox") >= 24, "emphasis sits under the typeface row");

        SameRow(fx, "BulletsButton", "NumbersButton");
        SameRow(fx, "AlignLeftButton", "AlignCenterButton", "AlignRightButton", "AlignJustifyButton");
        Assert.True(CenterY(fx, "AlignLeftButton") - CenterY(fx, "BulletsButton") >= 24, "alignment sits under the lists row");
    });

    [Fact]
    public void CutAndCopy_StackBesidePaste_AndTheResetsStackThreeHigh() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false);

        Assert.True(CenterY(fx, "CopyButton") - CenterY(fx, "CutButton") >= 20);
        Assert.InRange(CenterY(fx, "PasteButton"), CenterY(fx, "CutButton"), CenterY(fx, "CopyButton"));

        double note = CenterY(fx, "ResetNoteSizeButton"), workspace = CenterY(fx, "ResetWorkspaceSizesButton"), all = CenterY(fx, "ResetAllSizesButton");
        Assert.True(note < workspace && workspace < all);
    });

    [Fact]
    public void TheWholeHomeRibbon_FitsInTheDefaultWindow_WithNoChevrons() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false);
        var scroll = (ScrollViewer)fx.Window.FindName("RibbonScroll");

        Assert.Equal(1200, fx.Window.Width);
        Assert.Equal(0, scroll.ScrollableWidth);
        Assert.Equal(Visibility.Collapsed, ((UIElement)fx.Window.FindName("RibbonScrollRight")).Visibility);
    });

    [Fact]
    public void ThereIsNoScrollbar_ButAChevronAppearsWhereThereIsMoreToSee() => Ui.Run(() =>
    {
        using var fx = Fixture(autoHide: false);
        var scroll = (ScrollViewer)fx.Window.FindName("RibbonScroll");
        var left = (Button)fx.Window.FindName("RibbonScrollLeft");
        var right = (Button)fx.Window.FindName("RibbonScrollRight");
        Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility);

        fx.Window.Width = 1800;
        Ui.Settle();
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (left.Visibility, right.Visibility));

        fx.Window.Width = 520;
        Ui.Settle();
        Assert.Equal((Visibility.Collapsed, Visibility.Visible), (left.Visibility, right.Visibility));
        Assert.All(Ui.Descendants<ScrollBar>(scroll), bar => Assert.False(bar.IsVisible));

        Click(right);
        Ui.Settle();
        Assert.True(scroll.HorizontalOffset > 0);
        Assert.Equal(Visibility.Visible, left.Visibility);

        for (int i = 0; i < 20; i++) Click(right);
        Ui.Settle();
        Assert.Equal(Visibility.Collapsed, right.Visibility);
        Assert.Equal(Visibility.Visible, left.Visibility);

        Click(left);
        Ui.Settle();
        Assert.Equal(Visibility.Visible, right.Visibility);
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
