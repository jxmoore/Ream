using System.Windows;
using System.Windows.Input;
using Ream.App.Input;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class WidthNudgeTests
{
    [Theory]
    [InlineData(0.5, 1, 0.55)]
    [InlineData(0.5, -1, 0.45)]
    [InlineData(0.97, 1, 1.0)]
    [InlineData(1.0, 1, 1.0)]
    [InlineData(0.17, -1, 0.15)]
    [InlineData(0.15, -1, 0.15)]
    [InlineData(1d / 3d, 1, 0.3833)]
    public void OneStepIsFivePercent_KeptInRange(double from, int direction, double expected)
    {
        Assert.Equal(expected, WidthPresets.Nudge(from, direction), 4);
    }

    [Fact]
    public void RepeatedSteps_DoNotDriftFromFloatingPointError()
    {
        double width = 0.5;
        for (int i = 0; i < 6; i++) width = WidthPresets.Nudge(width, 1);
        Assert.Equal(0.8, width, 10);

        for (int i = 0; i < 6; i++) width = WidthPresets.Nudge(width, -1);
        Assert.Equal(0.5, width, 10);
    }
}

public class SizeCommandTests
{
    private static AppViewModel AppWith(double width)
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel { WidthFraction = width }], null);
        return new AppViewModel(new AppConfig(), [workspace]);
    }

    [Fact]
    public void SizeUp_WidensTheFocusedNoteByFivePercent()
    {
        var app = AppWith(0.5);

        app.SizeUpCommand.Execute(null);

        Assert.Equal(0.55, app.CurrentWorkspace.Notes[0].WidthFraction, 4);
    }

    [Fact]
    public void SizeDown_NarrowsIt()
    {
        var app = AppWith(0.5);

        app.SizeDownCommand.Execute(null);

        Assert.Equal(0.45, app.CurrentWorkspace.Notes[0].WidthFraction, 4);
    }

    [Fact]
    public void SizeChanges_StopAtTheLimits()
    {
        var wide = AppWith(0.98);
        wide.SizeUpCommand.Execute(null);
        wide.SizeUpCommand.Execute(null);
        Assert.Equal(1.0, wide.CurrentWorkspace.Notes[0].WidthFraction, 4);

        var narrow = AppWith(0.16);
        narrow.SizeDownCommand.Execute(null);
        narrow.SizeDownCommand.Execute(null);
        Assert.Equal(0.15, narrow.CurrentWorkspace.Notes[0].WidthFraction, 4);
    }

    [Fact]
    public void OnlyTheFocusedNoteChanges()
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel(), new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        app.CurrentWorkspace.SetFocus(1);

        app.SizeUpCommand.Execute(null);

        Assert.Equal(0.5, app.CurrentWorkspace.Notes[0].WidthFraction, 4);
        Assert.Equal(0.55, app.CurrentWorkspace.Notes[1].WidthFraction, 4);
    }

    [Fact]
    public void WithNothingFocused_ItIsHarmless()
    {
        var app = new AppViewModel(new AppConfig(), []);

        app.SizeUpCommand.Execute(null);
        app.SizeDownCommand.Execute(null);
    }

    [Fact]
    public void CyclingPresetsStillWorksAlongside()
    {
        var app = AppWith(0.5);

        app.SizeUpCommand.Execute(null); // 0.55, between presets
        app.CycleWidthPresetCommand.Execute(null);

        Assert.Equal(2d / 3d, app.CurrentWorkspace.Notes[0].WidthFraction, 4);
    }
}

public class KeyGestureTests
{
    [Theory]
    [InlineData("sizeUp", Key.OemPlus, ModifierKeys.Alt)]
    [InlineData("sizeDown", Key.OemMinus, ModifierKeys.Alt)]
    [InlineData("toggleAppFullscreen", Key.F11, ModifierKeys.None)]
    [InlineData("toggleFullscreen", Key.F11, ModifierKeys.Alt)]
    public void TheNewDefaults_ParseToTheKeysTheyName(string action, Key key, ModifierKeys modifiers)
    {
        var app = new AppViewModel(new AppConfig(), []);

        var bindings = KeyBindingsRegistry.Build(AppConfig.DefaultKeybindings(), app.Actions);

        var binding = bindings.Single(b => ReferenceEquals(b.Command, app.Actions[action]));
        Assert.Equal(key, binding.Key);
        Assert.Equal(modifiers, binding.Modifiers);
    }

    [Fact]
    public void EveryActionTheAppHasIsBoundByDefault_WithoutFallingBack()
    {
        var app = new AppViewModel(new AppConfig(), []);
        var defaults = AppConfig.DefaultKeybindings();

        var bindings = KeyBindingsRegistry.Build(defaults, app.Actions);

        Assert.Equal(app.Actions.Count, bindings.Count);
        Assert.All(app.Actions.Keys, action => Assert.True(defaults.ContainsKey(action), $"{action} has no default gesture"));
    }
}

public class AppFullscreenCommandTests
{
    [Fact]
    public void TheCommandAsksTheWindowToToggle()
    {
        var app = new AppViewModel(new AppConfig(), []);
        int requests = 0;
        app.AppFullscreenToggleRequested += () => requests++;

        app.ToggleAppFullscreenCommand.Execute(null);
        app.ToggleAppFullscreenCommand.Execute(null);

        Assert.Equal(2, requests);
    }

    [Fact]
    public void ItIsDistinctFromTheNoteFullscreenCommand()
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        int requests = 0;
        app.AppFullscreenToggleRequested += () => requests++;

        app.ToggleFullscreenCommand.Execute(null);

        Assert.Equal(0, requests);
        Assert.True(app.CurrentWorkspace.Notes[0].IsFullscreen);
    }

    [Fact]
    public void TheMainWindowBindsF11ToItAndAltF11ToTheNoteOne() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        var bindings = fx.Window.InputBindings.OfType<KeyBinding>().ToList();

        Assert.Same(fx.App.ToggleAppFullscreenCommand, bindings.Single(b => b.Key == Key.F11 && b.Modifiers == ModifierKeys.None).Command);
        Assert.Same(fx.App.ToggleFullscreenCommand, bindings.Single(b => b.Key == Key.F11 && b.Modifiers == ModifierKeys.Alt).Command);
        Assert.Same(fx.App.SizeUpCommand, bindings.Single(b => b.Key == Key.OemPlus && b.Modifiers == ModifierKeys.Alt).Command);
        Assert.Same(fx.App.SizeDownCommand, bindings.Single(b => b.Key == Key.OemMinus && b.Modifiers == ModifierKeys.Alt).Command);
    });
}

public class ViewModeCommandTests
{
    private static (AppViewModel App, NoteViewModel Note) Fixture()
    {
        var workspace = new WorkspaceViewModel("W");
        var note = new NoteViewModel();
        workspace.LoadNotes([note], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        return (app, note);
    }

    [Fact]
    public void ToggleReadMode_SetsFullscreenAndReadOnlyTogether_AndClearsBothOnASecondClick()
    {
        var (app, note) = Fixture();

        app.ToggleReadModeCommand.Execute(null);
        Assert.True(note.IsFullscreen);
        Assert.True(note.IsReadOnly);
        Assert.True(note.EffectiveReadOnly);

        app.ToggleReadModeCommand.Execute(null);
        Assert.False(note.IsFullscreen);
        Assert.False(note.IsReadOnly);
    }

    [Fact]
    public void ClearingFullscreen_AlwaysClearsReadOnlyToo()
    {
        var (app, note) = Fixture();
        app.ToggleReadModeCommand.Execute(null);

        note.IsFullscreen = false;

        Assert.False(note.IsReadOnly);
    }

    [Fact]
    public void ToggleDraftView_TogglesHideImagesOnTheFocusedNote()
    {
        var (app, note) = Fixture();

        app.ToggleDraftViewCommand.Execute(null);
        Assert.True(note.HideImages);

        app.ToggleDraftViewCommand.Execute(null);
        Assert.False(note.HideImages);
    }

    [Fact]
    public void ToggleOutlineView_TogglesIsOutlineView_WhichIsAlwaysReadOnly()
    {
        var (app, note) = Fixture();

        app.ToggleOutlineViewCommand.Execute(null);
        Assert.True(note.IsOutlineView);
        Assert.True(note.EffectiveReadOnly);

        app.ToggleOutlineViewCommand.Execute(null);
        Assert.False(note.IsOutlineView);
        Assert.False(note.EffectiveReadOnly);
    }

    [Fact]
    public void ToggleOnePage_HidesEveryOtherNote_AndRestoresThemOnASecondClick()
    {
        var workspace = new WorkspaceViewModel("W");
        var first = new NoteViewModel();
        var second = new NoteViewModel();
        workspace.LoadNotes([first, second], first.Id);
        var app = new AppViewModel(new AppConfig(), [workspace]);

        app.ToggleOnePageCommand.Execute(null);
        Assert.True(app.OnePageMode);
        Assert.False(first.IsHiddenByOnePage); // the focused one keeps its own size, it's not hidden
        Assert.True(second.IsHiddenByOnePage);
        Assert.False(first.IsFullscreen); // unlike Read Mode, One Page never enlarges the note that stays

        app.ToggleOnePageCommand.Execute(null);
        Assert.False(app.OnePageMode);
        Assert.False(first.IsHiddenByOnePage);
        Assert.False(second.IsHiddenByOnePage);
    }

    [Fact]
    public void OnePage_FollowsFocusToWhicheverNoteIsFocusedNext()
    {
        var workspace = new WorkspaceViewModel("W");
        var first = new NoteViewModel();
        var second = new NoteViewModel();
        workspace.LoadNotes([first, second], first.Id);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        app.ToggleOnePageCommand.Execute(null);
        Assert.False(first.IsHiddenByOnePage);
        Assert.True(second.IsHiddenByOnePage);

        app.FocusNoteBy(1);

        Assert.True(first.IsHiddenByOnePage);
        Assert.False(second.IsHiddenByOnePage);
    }

    [Fact]
    public void OnePage_FollowsAWorkspaceSwitchToo()
    {
        var w1 = new WorkspaceViewModel("Alpha");
        var noteA1 = new NoteViewModel();
        var noteA2 = new NoteViewModel();
        w1.LoadNotes([noteA1, noteA2], noteA1.Id);
        var w2 = new WorkspaceViewModel("Beta");
        var noteB = new NoteViewModel();
        w2.LoadNotes([noteB], null);
        var app = new AppViewModel(new AppConfig(), [w1, w2], currentIndex: 0);
        app.ToggleOnePageCommand.Execute(null);
        Assert.False(noteA1.IsHiddenByOnePage);
        Assert.True(noteA2.IsHiddenByOnePage);

        app.SwitchWorkspace(1);

        // Leaving a workspace restores everything in it - it isn't shown either way, so nothing should still
        // look narrowed down to one note if you scroll back to it without One Page being asked for there again.
        Assert.False(noteA2.IsHiddenByOnePage);
        Assert.False(noteB.IsHiddenByOnePage);
    }

    [Fact]
    public void TheThreeViewModes_AreMutuallyExclusive()
    {
        var (app, note) = Fixture();

        app.ToggleReadModeCommand.Execute(null);
        Assert.True(note.IsReadOnly);

        app.ToggleDraftViewCommand.Execute(null);
        Assert.True(note.HideImages);
        Assert.False(note.IsReadOnly);
        Assert.False(note.IsOutlineView);

        app.ToggleOutlineViewCommand.Execute(null);
        Assert.True(note.IsOutlineView);
        Assert.False(note.HideImages);
        Assert.False(note.IsReadOnly);

        app.ToggleReadModeCommand.Execute(null);
        Assert.True(note.IsReadOnly);
        Assert.False(note.IsOutlineView);
        Assert.False(note.HideImages);
    }
}

public class FullscreenControllerTests
{
    private sealed class FakeFrame : IWindowFrame
    {
        private WindowStyle _style = WindowStyle.SingleBorderWindow;
        private ResizeMode _resize = ResizeMode.CanResize;
        private WindowState _state = WindowState.Normal;
        private Rect _bounds = new(120, 80, 1200, 720);

        public List<string> Log { get; } = [];

        public WindowStyle Style
        {
            get => _style;
            set { _style = value; Log.Add($"style={value}"); }
        }

        public ResizeMode ResizeMode
        {
            get => _resize;
            set { _resize = value; Log.Add($"resize={value}"); }
        }

        public WindowState State
        {
            get => _state;
            set { _state = value; Log.Add($"state={value}"); }
        }

        public Rect NormalBounds
        {
            get => _bounds;
            set { _bounds = value; Log.Add("bounds"); }
        }
    }

    [Fact]
    public void Entering_MakesTheWindowBorderlessAndMaximized()
    {
        var frame = new FakeFrame();
        var controller = new FullscreenController(frame);

        controller.Enter();

        Assert.True(controller.IsFullscreen);
        Assert.Equal(WindowStyle.None, frame.Style);
        Assert.Equal(ResizeMode.NoResize, frame.ResizeMode);
        Assert.Equal(WindowState.Maximized, frame.State);
    }

    [Fact]
    public void Leaving_RestoresStyleStateAndPlace()
    {
        var frame = new FakeFrame();
        var controller = new FullscreenController(frame);
        var before = frame.NormalBounds;

        controller.Enter();
        controller.Exit();

        Assert.False(controller.IsFullscreen);
        Assert.Equal(WindowStyle.SingleBorderWindow, frame.Style);
        Assert.Equal(ResizeMode.CanResize, frame.ResizeMode);
        Assert.Equal(WindowState.Normal, frame.State);
        Assert.Equal(before, frame.NormalBounds);
    }

    [Fact]
    public void AWindowThatWasMaximized_IsMaximizedAgainAfterwards()
    {
        var frame = new FakeFrame { State = WindowState.Maximized };
        var controller = new FullscreenController(frame);
        frame.Log.Clear();

        controller.Enter();

        // Back to normal before the style changes, so the borderless maximize is a fresh one.
        Assert.True(frame.Log.IndexOf("state=Normal") < frame.Log.IndexOf("style=None"));

        controller.Exit();
        Assert.Equal(WindowState.Maximized, frame.State);
        Assert.Equal(WindowStyle.SingleBorderWindow, frame.Style);
    }

    [Fact]
    public void ThePlaceIsRestoredEvenIfTheWindowWasResizedUnderneath()
    {
        var frame = new FakeFrame();
        var controller = new FullscreenController(frame);
        var before = frame.NormalBounds;

        controller.Enter();
        frame.NormalBounds = new Rect(0, 0, 3840, 2160);
        controller.Exit();

        Assert.Equal(before, frame.NormalBounds);
    }

    [Fact]
    public void TogglingTwice_IsAWashAndEnteringTwiceDoesNotForgetTheOriginal()
    {
        var frame = new FakeFrame();
        var controller = new FullscreenController(frame);

        controller.Toggle();
        Assert.True(controller.IsFullscreen);
        controller.Enter();
        controller.Toggle();

        Assert.False(controller.IsFullscreen);
        Assert.Equal(WindowStyle.SingleBorderWindow, frame.Style);
        Assert.Equal(ResizeMode.CanResize, frame.ResizeMode);
    }

    [Fact]
    public void LeavingWhenNotFullscreen_TouchesNothing()
    {
        var frame = new FakeFrame();
        var controller = new FullscreenController(frame);

        controller.Exit();

        Assert.Empty(frame.Log);
    }
}
