using System.Windows;
using System.Windows.Controls;
using Ream.App.Controls;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

public class ConfigDefaultsTests
{
    [Fact]
    public void FocusedNoteIsCenteredByDefault()
    {
        Assert.True(new AppConfig().Layout.CenterFocusedColumn);
    }

    [Fact]
    public void CenteringCanStillBeTurnedOff()
    {
        Assert.False(new LayoutConfig { CenterFocusedColumn = false }.CenterFocusedColumn);
    }

    [Theory]
    [InlineData("toggleFullscreen", "Shift+F11")]
    [InlineData("renameWorkspace", "Shift+F2")]
    [InlineData("newNote", "Alt+N")]
    [InlineData("focusNextNote", "Alt+Right")]
    public void DefaultBindings_AreTheAgreedOnes(string action, string gesture)
    {
        Assert.Equal(gesture, AppConfig.DefaultKeybindings()[action]);
    }

    [Fact]
    public void EveryActionHasExactlyOneGesture_AndNoTwoActionsShareOne()
    {
        var defaults = AppConfig.DefaultKeybindings();

        Assert.All(defaults.Values, gesture => Assert.DoesNotContain(',', gesture));
        Assert.Equal(defaults.Count, defaults.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

public class CenteredLayoutTests
{
    private static AppConfig Instant => new() { Animations = new AnimationConfig { Enabled = false } };

    private static double Left(NoteColumnView view, NoteRowPanel row) => view.TranslatePoint(new Point(0, 0), row).X;

    private static double ExpectedCenteredLeft(NoteColumnView view, NoteRowPanel row) =>
        (row.ActualWidth - view.ActualWidth) / 2;

    [Fact]
    public void TheFirstNote_IsCenteredWhenTheAppOpens() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Instant, ("W", 3));
        var view = fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]);
        var row = Ui.Ancestor<NoteRowPanel>(view)!;

        Assert.Equal(ExpectedCenteredLeft(view, row), Left(view, row), 1);
    });

    [Fact]
    public void AMiddleNote_IsCenteredWhenFocused() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Instant, ("W", 5));
        var notes = fx.App.CurrentWorkspace.Notes;

        fx.App.CurrentWorkspace.SetFocus(2);
        Ui.Settle();

        var view = fx.ColumnOf(notes[2]);
        var row = Ui.Ancestor<NoteRowPanel>(view)!;
        Assert.Equal(ExpectedCenteredLeft(view, row), Left(view, row), 1);
    });

    [Fact]
    public void TheLastNote_IsCenteredToo_LeavingSpaceBesideIt() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Instant, ("W", 4));
        var notes = fx.App.CurrentWorkspace.Notes;

        fx.App.CurrentWorkspace.SetFocus(3);
        Ui.Settle();

        var view = fx.ColumnOf(notes[3]);
        var row = Ui.Ancestor<NoteRowPanel>(view)!;
        Assert.Equal(ExpectedCenteredLeft(view, row), Left(view, row), 1);
        Assert.True(Left(view, row) + view.ActualWidth < row.ActualWidth - 50);
    });

    [Fact]
    public void ANoteOfAnyWidth_IsCentered() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Instant, ("W", 3));
        var note = fx.App.CurrentWorkspace.Notes[0];

        note.WidthFraction = 0.3;
        Ui.Settle();

        var view = fx.ColumnOf(note);
        var row = Ui.Ancestor<NoteRowPanel>(view)!;
        Assert.Equal(ExpectedCenteredLeft(view, row), Left(view, row), 1);
    });

    [Fact]
    public void AFullWidthNote_FillsTheRowAsBefore() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Instant, ("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];

        note.WidthFraction = 1.0;
        Ui.Settle();

        var view = fx.ColumnOf(note);
        var row = Ui.Ancestor<NoteRowPanel>(view)!;
        Assert.Equal(new LayoutConfig().GapPx, Left(view, row), 1);
    });

    [Fact]
    public void WithCenteringOff_TheOldMinimalScrollingStillWorks() => Ui.Run(() =>
    {
        var config = new AppConfig
        {
            Layout = new LayoutConfig { CenterFocusedColumn = false },
            Animations = new AnimationConfig { Enabled = false },
        };
        using var fx = new WindowFixture(config, ("W", 3));
        var view = fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]);
        var row = Ui.Ancestor<NoteRowPanel>(view)!;

        Assert.Equal(new LayoutConfig().GapPx, Left(view, row), 1);
    });

    // ----- The color strip is gone -----

    [Fact]
    public void ANote_HasNoAccentStripAtTheTop() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(Instant, ("W", 1));
        var view = fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]);

        var grid = Ui.Descendants<Grid>(view).First(g => g.RowDefinitions.Count > 0);

        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.DoesNotContain(grid.RowDefinitions, r => r.Height.IsAbsolute && r.Height.Value == 4);
    });
}

public class TestHostTests
{
    [Fact]
    public void TheHarness_NeverStartsTheRealApp() => Ui.Run(() =>
    {
        Ui.Settle();

        // The real OnStartup would show a MainWindow bound to the user's own documents.
        Assert.Empty(Application.Current.Windows.OfType<Ream.App.MainWindow>());
        Assert.Equal("Dark.xaml", System.IO.Path.GetFileName(Application.Current.Resources.MergedDictionaries[0].Source.ToString()));
    });
}
