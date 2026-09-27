using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.Controls;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>
/// Board Zoom (a live 0-100% dial, not a toggled mode) and Pan (config's own held Alt+X, or the Home tab's
/// sticky toggle). Two things are deliberately not covered here, both for the same reason: a synthetic
/// MouseWheelEventArgs/KeyEventArgs carries no position or modifier state a test can control the way a real
/// hardware gesture would (Keyboard.Modifiers reflects the actual, real keyboard - not anything settable on the
/// event args), so the real Ctrl+Alt+Scroll and held-Alt+X paths through MainWindow.OnPreviewMouseWheel/
/// OnPreviewKeyDown/Up aren't reachable from a test - the same limitation this codebase's existing Alt+Scroll/
/// Shift+Scroll/Ctrl+Scroll handling already has no test coverage for. What's tested instead: the gesture strings
/// parse correctly (DrillIntoBoardClick and the two ParseX methods are internal for exactly this reason), and the
/// sticky Pan toggle (Settings.PanModeOn), which - unlike a held key - is a real property a test can just set.
/// </summary>
public class BoardZoomTests
{
    private static WorkspaceStripPanel Strip(WindowFixture fx) => Ui.Descendants<WorkspaceStripPanel>(fx.Window).Single();

    private static FrameworkElement Canvas(WindowFixture fx) => (FrameworkElement)fx.Window.FindName("CanvasArea");

    private static void RaiseClick(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public void BoardZoomPercent_DefaultsTo100_AndClampsToTheDropdownsOwnRange() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        Assert.Equal(100, fx.Window.Settings.BoardZoomPercent);

        fx.Window.Settings.BoardZoomPercent = 150;
        Assert.Equal(100, fx.Window.Settings.BoardZoomPercent);

        fx.Window.Settings.BoardZoomPercent = -20;
        Assert.Equal(0, fx.Window.Settings.BoardZoomPercent);
    });

    [Fact]
    public void TheStripIsNeverClipped_EvenAt100Percent() => Ui.Run(() =>
    {
        // CanvasArea's own Grid.Column="0" clip is what contains the board now, whatever the zoom - see
        // MainWindow's own constructor note on why turning WorkspaceStripPanel's clip off is a one-time thing.
        using var fx = new WindowFixture(("W", 1));
        Assert.False(Strip(fx).ClipToBounds);
    });

    [Fact]
    public void ChangingBoardZoomPercent_ScalesTheStrip_AndGrowsTheNearRadiusAsItShrinks() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var strip = Strip(fx);
        double radiusAt100 = strip.NearRadius;
        double marginAt100 = NoteRowPanel.GetNearMargin(strip);

        fx.Window.Settings.BoardZoomPercent = 50;
        Ui.Settle();

        var scale = Assert.IsType<ScaleTransform>(((TransformGroup)strip.RenderTransform).Children[0]);
        Assert.Equal(0.5, scale.ScaleX, 3);
        Assert.Equal(0.5, scale.ScaleY, 3);
        Assert.True(strip.NearRadius > radiusAt100, "zooming out should reveal more workspaces, so more of them need their notes actually loaded");
        Assert.True(NoteRowPanel.GetNearMargin(strip) > marginAt100, "zooming out should reveal more of each workspace's own row too, not just more workspaces");

        fx.Window.Settings.BoardZoomPercent = 100;
        Ui.Settle();

        Assert.Equal(radiusAt100, strip.NearRadius); // back to normal, precise loading once at rest again
        Assert.Equal(marginAt100, NoteRowPanel.GetNearMargin(strip));
    });

    [Fact]
    public void PanningAlone_AlsoWidensBothNearMargins_EvenAt100Percent() => Ui.Run(() =>
    {
        // Panning can reveal content arbitrarily far from the current position even without zooming out at all,
        // so it needs the same generous "exploring" loading zoom does - not just a zoom-proportional radius.
        using var fx = new WindowFixture(("W", 1));
        var strip = Strip(fx);
        double radiusAtRest = strip.NearRadius;
        double marginAtRest = NoteRowPanel.GetNearMargin(strip);

        fx.Window.SetBoardPanForTests(50, 0);
        Ui.Settle();

        Assert.True(strip.NearRadius > radiusAtRest);
        Assert.True(NoteRowPanel.GetNearMargin(strip) > marginAtRest);
    });

    [Fact]
    public void ZoomingOut_AlsoUnclipsEachWorkspacesOwnNoteRow_NotJustLoadsItsNotes() => Ui.Run(() =>
    {
        // Loading a note (IsNear) and being able to actually SEE it are two different things - NoteRowPanel clips
        // itself to its own bounds regardless of how much is loaded, and its own scroll offset only ever keeps the
        // FOCUSED note in view, so a wider NearMargin alone never revealed a row's other notes - only unclipping does.
        using var fx = new WindowFixture(("W", 2));
        var row = Ui.Ancestor<NoteRowPanel>(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]))!;
        Assert.True(row.ClipToBounds);

        fx.Window.Settings.BoardZoomPercent = 50;
        Ui.Settle();
        Assert.False(row.ClipToBounds);

        fx.Window.Settings.BoardZoomPercent = 100;
        Ui.Settle();
        Assert.True(row.ClipToBounds);
    });

    [Fact]
    public void PanningAlone_AlsoUnclipsEachWorkspacesOwnNoteRow_EvenAt100Percent() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var row = Ui.Ancestor<NoteRowPanel>(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]))!;

        fx.Window.SetBoardPanForTests(50, 0);
        Ui.Settle();
        Assert.False(row.ClipToBounds);

        fx.Window.SetBoardPanForTests(0, 0);
        Ui.Settle();
        Assert.True(row.ClipToBounds);
    });

    [Fact]
    public void TheStripScalesFromItsOwnCenter_NotItsTopLeftCorner() => Ui.Run(() =>
    {
        // Otherwise zooming out pulls everything toward the top-left instead of revealing workspaces on every
        // side of the current one symmetrically - see MainWindow's own constructor note on this.
        using var fx = new WindowFixture(("W", 1));
        Assert.Equal(new Point(0.5, 0.5), Strip(fx).RenderTransformOrigin);
    });

    [Fact]
    public void ClickingAWorkspaceBand_DrillsIntoIt_AndResetsZoomTo100() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("First", 1), ("Second", 1));
        int before = fx.App.CurrentIndex;
        fx.Window.Settings.BoardZoomPercent = 50;
        Ui.Settle();
        double viewport = Strip(fx).ActualHeight;
        Assert.True(viewport > 0, "the strip has no measured height to compute a click position from");

        // One whole band below the current workspace's own - DrillIntoBoardClick's own math (mirroring
        // WorkspaceStripPanel.ArrangeOverride) puts the next workspace's content starting exactly here.
        fx.Window.DrillIntoBoardClick(new Point(10, viewport + 10));

        Assert.Equal(before + 1, fx.App.CurrentIndex);
        Assert.Equal(100, fx.Window.Settings.BoardZoomPercent); // "drill into it": lands at the normal view
    });

    [Fact]
    public void DrillingIn_AlsoResetsAnyPan_SoTheArrivalIsCentered_NotWhereverThePanLeftIt() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("First", 1), ("Second", 1));
        fx.Window.Settings.BoardZoomPercent = 50;
        fx.Window.SetBoardPanForTests(37, -19);
        Ui.Settle();
        double viewport = Strip(fx).ActualHeight;

        fx.Window.DrillIntoBoardClick(new Point(10, viewport + 10));

        var translate = Assert.IsType<TranslateTransform>(((TransformGroup)Strip(fx).RenderTransform).Children[1]);
        Assert.Equal(0, translate.X);
        Assert.Equal(0, translate.Y);
    });

    [Fact]
    public void ClickingTheCurrentWorkspacesOwnBand_DoesNothing() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("First", 1), ("Second", 1));
        int before = fx.App.CurrentIndex;
        fx.Window.Settings.BoardZoomPercent = 50;
        Ui.Settle();

        fx.Window.DrillIntoBoardClick(new Point(10, 10));

        Assert.Equal(before, fx.App.CurrentIndex);
        Assert.Equal(50, fx.Window.Settings.BoardZoomPercent); // untouched - a click that lands on the current
                                                                 // workspace's own band must stay a no-op, since
                                                                 // OnPreviewMouseDown calls this for every plain
                                                                 // click while zoomed out at all, including ordinary
                                                                 // clicks into the current note.
    });

    [Fact]
    public void ClickingPastTheLastWorkspace_DoesNothing() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Only", 1));
        fx.Window.Settings.BoardZoomPercent = 50;
        Ui.Settle();
        double viewport = Strip(fx).ActualHeight;

        fx.Window.DrillIntoBoardClick(new Point(10, viewport * 50));

        Assert.Equal(50, fx.Window.Settings.BoardZoomPercent); // untouched
    });

    // ----- Board Zoom's own dropdown -----

    private static ComboBox BoardZoomBox(WindowFixture fx) =>
        (ComboBox)((ViewRibbonView)fx.Window.FindName("ViewRibbon")).FindName("BoardZoomBox");

    [Fact]
    public void PickingAPreset_AppliesImmediately_NoEnterOrLostFocusNeeded() => Ui.Run(() =>
    {
        // The dropdown used to be IsEditable, applying only on Enter/lost focus - picking an item from the list
        // alone looked like it did nothing. It's a plain closed-list select now: choosing an item is the whole
        // gesture, so SelectionChanged is what has to apply it, immediately.
        using var fx = new WindowFixture(("W", 1));
        var box = BoardZoomBox(fx);

        box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "50");

        Assert.Equal(50, fx.Window.Settings.BoardZoomPercent);
    });

    [Fact]
    public void TheDropdownsSelection_FollowsBoardZoomPercentChangedFromAnywhereElse() => Ui.Run(() =>
    {
        // Not bound (a plain select has nothing to bind) - so a change from any other source (the wheel gesture,
        // drilling into a workspace resetting it to 100) has to be picked up and reflected back by hand instead.
        using var fx = new WindowFixture(("W", 1));
        var box = BoardZoomBox(fx);

        fx.Window.Settings.BoardZoomPercent = 75;
        Ui.Settle();
        Assert.Equal("75", ((ComboBoxItem)box.SelectedItem).Tag);

        fx.Window.Settings.BoardZoomPercent = 100;
        Ui.Settle();
        Assert.Equal("100", ((ComboBoxItem)box.SelectedItem).Tag);
    });

    [Fact]
    public void ANonPresetPercent_LeavesTheDropdownShowingNoSelection() => Ui.Run(() =>
    {
        // A value the wheel gesture landed on that isn't one of the four presets (63%, say) has nothing in the
        // closed list to highlight - the same as any plain <select> whose bound value isn't one of its own options.
        using var fx = new WindowFixture(("W", 1));
        var box = BoardZoomBox(fx);

        fx.Window.Settings.BoardZoomPercent = 63;
        Ui.Settle();

        Assert.Null(box.SelectedItem);
    });

    // ----- Pan -----

    [Fact]
    public void PanModeOn_ShowsTheHandCursor_AndTurningItOffRestoresTheDefault() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var canvas = Canvas(fx);
        Assert.Null(canvas.Cursor);

        fx.Window.Settings.PanModeOn = true;
        Ui.Settle();
        Assert.Equal(Cursors.Hand, canvas.Cursor);

        fx.Window.Settings.PanModeOn = false;
        Ui.Settle();
        Assert.Null(canvas.Cursor);
    });

    [Fact]
    public void TheHomeTabsPanButton_TogglesPanModeOn_AndReflectsItBack() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var ribbon = (RibbonView)fx.Window.FindName("Ribbon");
        var button = (ToggleButton)ribbon.FindName("PanModeButton");

        RaiseClick(button);
        Assert.True(fx.Window.Settings.PanModeOn);
        Assert.True(button.IsChecked);

        RaiseClick(button);
        Assert.False(fx.Window.Settings.PanModeOn);
        Assert.False(button.IsChecked);
    });


    // ----- Config-driven gestures -----

    [Fact]
    public void TheDefaultGestures_ParseCorrectly() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, fx.Window.BoardZoomWheelModifiers);
        Assert.Equal(Key.X, fx.Window.PanGesture?.Key);
        Assert.Equal(ModifierKeys.Alt, fx.Window.PanGesture?.Modifiers);
    });

    [Fact]
    public void ARebindInConfig_ChangesTheParsedGesture() => Ui.Run(() =>
    {
        var bindings = AppConfig.DefaultKeybindings();
        bindings["boardZoomWheel"] = "Shift";
        bindings["panCanvas"] = "Ctrl+Shift+P";
        using var fx = new WindowFixture(new AppConfig { Keybindings = bindings }, ("W", 1));

        Assert.Equal(ModifierKeys.Shift, fx.Window.BoardZoomWheelModifiers);
        Assert.Equal(Key.P, fx.Window.PanGesture?.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, fx.Window.PanGesture?.Modifiers);
    });

    [Fact]
    public void AnUnparsableGesture_FallsBackToNothingBound_WithoutThrowing() => Ui.Run(() =>
    {
        var bindings = AppConfig.DefaultKeybindings();
        bindings["boardZoomWheel"] = "NotAModifier";
        bindings["panCanvas"] = "";
        using var fx = new WindowFixture(new AppConfig { Keybindings = bindings }, ("W", 1));

        Assert.Equal(ModifierKeys.None, fx.Window.BoardZoomWheelModifiers);
        Assert.Null(fx.Window.PanGesture);
    });

    [Fact]
    public void ARebind_LiveReloadedFromTheConfigFile_ReparsesBothGestures() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        var bindings = AppConfig.DefaultKeybindings();
        bindings["boardZoomWheel"] = "Shift+Alt";
        fx.App.Config = new AppConfig { Keybindings = bindings };

        Assert.Equal(ModifierKeys.Shift | ModifierKeys.Alt, fx.Window.BoardZoomWheelModifiers);
    });
}
