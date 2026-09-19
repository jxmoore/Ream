using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.Controls;
using Ream.App.Input;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class PhaseOneConfigTests
{
    [Fact]
    public void TheDefaultGap_IsWiderThanBefore()
    {
        Assert.Equal(28, new AppConfig().Layout.GapPx);
        Assert.Equal(28, new LayoutConfig().GapPx);
    }

    [Fact]
    public void FocusingTheFirstNoteOnSwitch_IsOnByDefault()
    {
        Assert.True(new AppConfig().Layout.FocusFirstNoteOnSwitch);
    }

    [Fact]
    public void TheNewSettings_AreReadFromTheFile()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, """{ "layout": { "gapPx": 40, "focusFirstNoteOnSwitch": false } }""");

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out _));

        Assert.Equal(40, config.Layout.GapPx);
        Assert.False(config.Layout.FocusFirstNoteOnSwitch);
    }

    [Fact]
    public void AFileThatSaysNothingAboutThem_GetsTheDefaults()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, """{ "layout": { "centerFocusedColumn": true } }""");

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out _));

        Assert.Equal(28, config.Layout.GapPx);
        Assert.True(config.Layout.FocusFirstNoteOnSwitch);
    }

    [Theory]
    [InlineData("resetNoteSize", "Alt+Shift+R")]
    [InlineData("resetWorkspaceSizes", "Ctrl+Alt+R")]
    [InlineData("resetAllSizes", "Ctrl+Alt+Shift+R")]
    public void TheResetActions_HaveTheirDefaultKeys(string action, string gesture)
    {
        Assert.Equal(gesture, AppConfig.DefaultKeybindings()[action]);
    }

    [Theory]
    [InlineData("resetNoteSize", Key.R, ModifierKeys.Alt | ModifierKeys.Shift)]
    [InlineData("resetWorkspaceSizes", Key.R, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData("resetAllSizes", Key.R, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)]
    public void TheResetKeys_ParseToTheKeysTheyName(string action, Key key, ModifierKeys modifiers)
    {
        var app = new AppViewModel(new AppConfig(), []);

        var bindings = KeyBindingsRegistry.Build(AppConfig.DefaultKeybindings(), app.Actions);

        var binding = bindings.Single(b => ReferenceEquals(b.Command, app.Actions[action]));
        Assert.Equal(key, binding.Key);
        Assert.Equal(modifiers, binding.Modifiers);
    }

    [Fact]
    public void AltPlusTheDigitKeys_AreKeptFreeForJumpingToWorkspaces()
    {
        var app = new AppViewModel(new AppConfig(), []);
        var bindings = KeyBindingsRegistry.Build(AppConfig.DefaultKeybindings(), app.Actions);
        var digits = new[]
        {
            Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9,
            Key.NumPad0, Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4, Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9,
        };

        Assert.NotEmpty(bindings);
        Assert.DoesNotContain(bindings, b => b.Modifiers == ModifierKeys.Alt && digits.Contains(b.Key));
    }

    [Fact]
    public void TheResetKeys_FormOneFamilyOnR_NextToCycleWidth()
    {
        var keys = AppConfig.DefaultKeybindings();

        Assert.Equal("Alt+R", keys["cycleWidthPreset"]);
        Assert.All(new[] { "resetNoteSize", "resetWorkspaceSizes", "resetAllSizes" }, a => Assert.EndsWith("+R", keys[a]));
        Assert.Equal(keys.Count, keys.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheResetActions_AreDescribedInHelp()
    {
        var entries = HelpContent.Build(AppConfig.DefaultKeybindings()).SelectMany(s => s.Entries).ToList();

        Assert.Contains(entries, e => e.Description.Contains("this note's width") && e.Gesture == "Alt + Shift + R");
        Assert.Contains(entries, e => e.Description.Contains("in this workspace") && e.Gesture == "Ctrl + Alt + R");
        Assert.Contains(entries, e => e.Description.Contains("every workspace") && e.Gesture == "Ctrl + Alt + Shift + R");
    }

    [Theory]
    [InlineData(0.5, "50%")]
    [InlineData(0.55, "55%")]
    [InlineData(1d / 3d, "33%")]
    [InlineData(2d / 3d, "67%")]
    [InlineData(1.0, "100%")]
    [InlineData(0.15, "15%")]
    public void WidthsAreShownAsPercentages(double fraction, string expected)
    {
        Assert.Equal(expected, WidthPresets.Percent(fraction));
    }
}

public class FocusFirstNoteOnSwitchTests
{
    private static WorkspaceViewModel Workspace(string name, int notes)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => new NoteViewModel { Title = $"{name}{i}" }), null);
        return workspace;
    }

    private static AppConfig Config(bool focusFirst) =>
        new() { Layout = new LayoutConfig { FocusFirstNoteOnSwitch = focusFirst } };

    private static AppViewModel App(AppConfig config, params WorkspaceViewModel[] workspaces) => new(config, workspaces);

    [Fact]
    public void GoingDown_LandsOnTheFirstNote_EvenIfAnotherWasFocusedThere()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 4);
        b.SetFocus(2);
        var app = App(Config(true), a, b);

        app.SwitchWorkspaceDownCommand.Execute(null);

        Assert.Same(b, app.CurrentWorkspace);
        Assert.Equal(0, b.FocusedIndex);
        Assert.True(b.Notes[0].IsFocused);
        Assert.False(b.Notes[2].IsFocused);
    }

    [Fact]
    public void GoingBackUp_AlsoStartsAtTheFirstNote()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 2);
        a.SetFocus(2);
        var app = App(Config(true), a, b);
        app.SwitchWorkspaceDownCommand.Execute(null);
        a.SetFocus(2);

        app.SwitchWorkspaceUpCommand.Execute(null);

        Assert.Equal(0, a.FocusedIndex);
    }

    [Fact]
    public void TheWheelAndTheWorkspaceMenu_DoTheSame()
    {
        var a = Workspace("A", 2);
        var b = Workspace("B", 3);
        var c = Workspace("C", 3);
        b.SetFocus(2);
        c.SetFocus(1);
        var app = App(Config(true), a, b, c);

        app.SwitchWorkspace(1);
        Assert.Equal(0, b.FocusedIndex);

        app.SelectWorkspaceCommand.Execute(c);
        Assert.Equal(0, c.FocusedIndex);
    }

    [Fact]
    public void WithTheOptionOff_EachWorkspaceRemembersWhereYouWere()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 4);
        b.SetFocus(2);
        var app = App(Config(false), a, b);

        app.SwitchWorkspaceDownCommand.Execute(null);

        Assert.Equal(2, b.FocusedIndex);
    }

    [Fact]
    public void MovingANoteToAnotherWorkspace_KeepsItFocusedThere()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 3);
        b.SetFocus(2);
        var app = App(Config(true), a, b);
        var moved = a.FocusedNote!;

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Same(b, app.CurrentWorkspace);
        Assert.Same(moved, b.FocusedNote);
        Assert.Equal(0, b.FocusedIndex);
    }

    [Fact]
    public void SwitchingToAnEmptyWorkspace_IsFine()
    {
        var a = Workspace("A", 2);
        var app = App(Config(true), a);

        app.SwitchWorkspaceUpCommand.Execute(null);

        Assert.True(app.CurrentWorkspace.IsEmpty);
        Assert.Null(app.CurrentWorkspace.FocusedNote);
    }

    [Fact]
    public void ARequestForTheEditor_StillFollowsASwitch()
    {
        var app = App(Config(true), Workspace("A", 1), Workspace("B", 1));
        int requests = 0;
        app.FocusEditorRequested += () => requests++;

        app.SwitchWorkspace(1);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void ABlankDraftInTheWorkspaceYouLeft_StillVanishes()
    {
        var a = Workspace("A", 1);
        var b = Workspace("B", 2);
        var app = App(Config(true), a, b);
        app.NewNoteCommand.Execute(null);
        Assert.Equal(2, a.Notes.Count);

        app.SwitchWorkspace(1);

        Assert.Single(a.Notes);
        Assert.Equal(0, b.FocusedIndex);
    }
}

public class ResetSizeTests
{
    private static WorkspaceViewModel Workspace(string name, params double[] widths)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(widths.Select(w => new NoteViewModel { WidthFraction = w }), null);
        return workspace;
    }

    private static double[] Widths(WorkspaceViewModel w) => w.Notes.Select(n => n.WidthFraction).ToArray();

    [Fact]
    public void ResetNote_OnlyTouchesTheFocusedNote()
    {
        var a = Workspace("A", 0.3, 0.8, 0.6);
        var app = new AppViewModel(new AppConfig(), [a]);
        a.SetFocus(1);

        app.ResetNoteSizeCommand.Execute(null);

        Assert.Equal([0.3, 0.5, 0.6], Widths(a));
    }

    [Fact]
    public void ResetWorkspace_TouchesEveryNoteHere_AndNothingElsewhere()
    {
        var a = Workspace("A", 0.3, 0.8, 0.6);
        var b = Workspace("B", 0.9, 0.2);
        var app = new AppViewModel(new AppConfig(), [a, b]);

        app.ResetWorkspaceSizesCommand.Execute(null);

        Assert.Equal([0.5, 0.5, 0.5], Widths(a));
        Assert.Equal([0.9, 0.2], Widths(b));
    }

    [Fact]
    public void ResetAll_TouchesEveryNoteInEveryWorkspace()
    {
        var a = Workspace("A", 0.3, 0.8);
        var b = Workspace("B", 0.9, 0.2);
        var c = Workspace("C", 1.0);
        var app = new AppViewModel(new AppConfig(), [a, b, c]);

        app.ResetAllSizesCommand.Execute(null);

        Assert.All(new[] { a, b, c }, w => Assert.All(Widths(w), width => Assert.Equal(0.5, width)));
    }

    [Fact]
    public void TheDefaultIsHalfTheRow()
    {
        Assert.Equal(0.5, WidthPresets.Default);
    }

    [Fact]
    public void ResetsAreHarmless_WhenThereIsNothingToReset()
    {
        var app = new AppViewModel(new AppConfig(), []);

        app.ResetNoteSizeCommand.Execute(null);
        app.ResetWorkspaceSizesCommand.Execute(null);
        app.ResetAllSizesCommand.Execute(null);

        Assert.True(app.CurrentWorkspace.IsEmpty);
    }

    [Fact]
    public void ResetAll_ReachesNotesInWorkspacesThatWereNeverOpened()
    {
        var a = Workspace("A", 0.3);
        var b = Workspace("B", 0.9);
        var app = new AppViewModel(new AppConfig(), [a, b]);

        app.ResetAllSizesCommand.Execute(null);
        app.SwitchWorkspace(1);

        Assert.Equal([0.5], Widths(b));
    }

    [Fact]
    public void TheResetActions_AreBoundToTheirCommands()
    {
        var app = new AppViewModel(new AppConfig(), []);

        Assert.Same(app.ResetNoteSizeCommand, app.Actions["resetNoteSize"]);
        Assert.Same(app.ResetWorkspaceSizesCommand, app.Actions["resetWorkspaceSizes"]);
        Assert.Same(app.ResetAllSizesCommand, app.Actions["resetAllSizes"]);
    }
}

public class SizeToastTests
{
    private static (AppViewModel App, NoteViewModel Note, List<string> Toasts) Make(double width = 0.5)
    {
        var workspace = new WorkspaceViewModel("W");
        var note = new NoteViewModel { WidthFraction = width };
        workspace.LoadNotes([note], null);
        var toasts = new List<string>();
        note.SizeToastRequested += toasts.Add;
        return (new AppViewModel(new AppConfig(), [workspace]), note, toasts);
    }

    [Fact]
    public void SizingUpAndDown_ShowsTheNewPercentage()
    {
        var (app, _, toasts) = Make(0.5);

        app.SizeUpCommand.Execute(null);
        app.SizeDownCommand.Execute(null);
        app.SizeDownCommand.Execute(null);

        Assert.Equal(["55%", "50%", "45%"], toasts);
    }

    [Fact]
    public void CyclingPresets_ShowsThePreset()
    {
        var (app, _, toasts) = Make(0.5);

        app.CycleWidthPresetCommand.Execute(null);

        Assert.Equal(["67%"], toasts);
    }

    [Fact]
    public void AtTheLimit_ItStillFlashes_SoYouKnowYouHitIt()
    {
        var (app, _, toasts) = Make(1.0);

        app.SizeUpCommand.Execute(null);

        Assert.Equal(["100%"], toasts);
    }

    [Fact]
    public void Resets_ShowTheDefault()
    {
        var (app, _, toasts) = Make(0.9);

        app.ResetNoteSizeCommand.Execute(null);
        app.ResetWorkspaceSizesCommand.Execute(null);
        app.ResetAllSizesCommand.Execute(null);

        Assert.Equal(["50%", "50%", "50%"], toasts);
    }

    [Fact]
    public void ChangingTheWidthInCode_DoesNotFlashAnything()
    {
        var (_, note, toasts) = Make();

        note.WidthFraction = 0.7;

        Assert.Empty(toasts);
    }

    [Fact]
    public void ANoteNoLongerCarriesAWidthLabel()
    {
        Assert.Null(typeof(NoteViewModel).GetProperty("WidthLabel"));
    }
}

public class SizeToastInTheWindowTests
{
    private static readonly AppConfig Animated = new();

    private static TextBlock ToastOf(NoteColumnView view) => Ui.Descendants<TextBlock>(view).Single(t => t.Name == "SizeToast");

    [Fact]
    public void TheHeader_NoLongerShowsTheWidth() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var view = fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]);

        var texts = Ui.Descendants<TextBlock>(view).Select(t => t.Text).ToList();

        Assert.DoesNotContain(texts, t => t is "1/2" or "50%" or "Full");
        Assert.Equal("", ToastOf(view).Text);
        Assert.Equal(0, ToastOf(view).Opacity);
    });

    [Fact]
    public void SizingUp_FlashesThePercentage_InTheAccentColor() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Animated, ("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var toast = ToastOf(fx.ColumnOf(note));

        fx.App.SizeUpCommand.Execute(null);
        Ui.Settle();

        Assert.Equal("55%", toast.Text);
        Assert.Equal(1, toast.Opacity, 2);
        Assert.Equal(Themes.Brush(ThemeServiceKey), ((System.Windows.Media.SolidColorBrush)toast.Foreground).Color);
    });

    private const string ThemeServiceKey = "FocusBorderBrush";

    [Fact]
    public void ThePercentage_FadesAwayByItself() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Animated, ("W", 2));
        var toast = ToastOf(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]));

        fx.App.SizeUpCommand.Execute(null);
        Ui.Settle();
        Assert.True(toast.Opacity > 0.9);

        Thread.Sleep(1500);
        Ui.Settle();

        Assert.Equal(0, toast.Opacity, 2);
    });

    [Fact]
    public void WithAnimationsOff_ItStillGoesAway() => Ui.Run(() =>
    {
        var config = new AppConfig { Animations = new AnimationConfig { Enabled = false } };
        using var fx = new WindowFixture(config, ("W", 2));
        var toast = ToastOf(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]));

        fx.App.SizeDownCommand.Execute(null);
        Ui.Settle();
        Assert.Equal(1, toast.Opacity, 2);

        Thread.Sleep(1200);
        Ui.Settle();

        Assert.Equal(0, toast.Opacity, 2);
    });

    [Fact]
    public void PressingAgain_RestartsTheFlash_WithTheNewValue() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Animated, ("W", 2));
        var toast = ToastOf(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]));

        fx.App.SizeUpCommand.Execute(null);
        Ui.Settle();
        Thread.Sleep(600);
        fx.App.SizeUpCommand.Execute(null);
        Ui.Settle();
        Thread.Sleep(600);
        Ui.Settle();

        Assert.Equal("60%", toast.Text);
        Assert.True(toast.Opacity > 0.9, "the second press should have restarted the hold");
    });
}

public class WorkspaceSwitchSnapTests
{
    private static double Left(NoteColumnView view, NoteRowPanel row) => view.TranslatePoint(new Point(0, 0), row).X;

    [Fact]
    public void ArrivingOnTheFirstNote_DoesNotShowTheRowSlidingSideways() => Ui.Run(() =>
    {
        // Animations ON: a row that scrolled itself while the workspace slid in would still be mid-way here.
        using var fx = new WindowFixture(("A", 3), ("B", 6));
        var b = fx.App.Workspaces[2];
        b.SetFocus(5);
        Ui.Settle();
        Ui.Settle();

        fx.App.SwitchWorkspace(1);
        Ui.Settle();

        var first = fx.ColumnOf(b.Notes[0]);
        var row = Ui.Ancestor<NoteRowPanel>(first)!;
        double centered = (row.ActualWidth - first.ActualWidth) / 2;
        Assert.Equal(0, b.FocusedIndex);
        Assert.Equal(centered, Left(first, row), 1);
    });

    [Fact]
    public void WithinTheCurrentWorkspace_MovingFocusStillScrollsSmoothly() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("A", 6));
        var a = fx.App.CurrentWorkspace;
        Ui.Settle();
        Ui.Settle();

        a.SetFocus(5);
        Ui.Settle();

        var last = fx.ColumnOf(a.Notes[5]);
        var row = Ui.Ancestor<NoteRowPanel>(last)!;
        double centered = (row.ActualWidth - last.ActualWidth) / 2;
        Assert.NotEqual(centered, Left(last, row), 1);
    });

    [Fact]
    public void OnlyTheCurrentWorkspaceRow_IsFlaggedCurrent() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("A", 2), ("B", 2));

        var rows = Ui.Descendants<NoteRowPanel>(fx.Window).ToList();
        var flags = rows.Select(r => r.IsCurrentWorkspace).ToList();

        Assert.Equal(1, flags.Count(f => f));
        Assert.True(Ui.Ancestor<NoteRowPanel>(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]))!.IsCurrentWorkspace);
    });
}
