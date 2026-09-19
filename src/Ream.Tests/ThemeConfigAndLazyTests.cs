using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.Controls;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Layout;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>Runs a test body with the app palette forced to a theme, and always puts the dark default back.</summary>
internal static class Themes
{
    public static void Use(string setting, Action<ThemeService> body)
    {
        var theme = new ThemeService(Application.Current, () => false);
        try
        {
            theme.Apply(setting);
            Ui.Settle();
            body(theme);
        }
        finally
        {
            new ThemeService(Application.Current, () => false).Apply("dark");
            Ui.Settle();
        }
    }

    public static Color Brush(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;
}

public class ThemeTests
{
    private const string Plain = """<ReamNote schemaVersion="1"><Doc><P><R>hello world</R></P></Doc></ReamNote>""";

    private static readonly Uri Light = new("/Ream.App;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri Dark = new("/Ream.App;component/Themes/Dark.xaml", UriKind.Relative);

    [Fact]
    public void BothPalettes_DefineTheSameBrushes() => Ui.Run(() =>
    {
        var dark = new ResourceDictionary { Source = Dark };
        var light = new ResourceDictionary { Source = Light };

        Assert.Equal(
            dark.Keys.Cast<string>().OrderBy(k => k),
            light.Keys.Cast<string>().OrderBy(k => k));
        Assert.True(dark.Count >= 20);
    });

    [Fact]
    public void ThePalettes_ActuallyDiffer() => Ui.Run(() =>
    {
        var dark = new ResourceDictionary { Source = Dark };
        var light = new ResourceDictionary { Source = Light };

        foreach (var key in new[] { "WindowBackgroundBrush", "CardBrush", "TextBrush", "ControlBrush" })
            Assert.NotEqual(((SolidColorBrush)dark[key]).Color, ((SolidColorBrush)light[key]).Color);
    });

    [Fact]
    public void Applying_SwapsThePalette_AndRaisesChangedOnlyOnRealChanges() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current, () => true);
        var changes = new List<bool>();
        theme.Changed += changes.Add;
        try
        {
            theme.Apply("light");
            var lightCard = Themes.Brush("CardBrush");
            theme.Apply("light");
            theme.Apply("dark");
            var darkCard = Themes.Brush("CardBrush");

            Assert.Equal([true, false], changes);
            Assert.NotEqual(lightCard, darkCard);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void SystemSetting_FollowsWindows_AndRefreshPicksUpAChange() => Ui.Run(() =>
    {
        bool systemLight = true;
        var theme = new ThemeService(Application.Current, () => systemLight);
        try
        {
            theme.Apply("system");
            Assert.True(theme.IsLight);

            systemLight = false;
            Assert.True(theme.IsLight);
            theme.Refresh();
            Assert.False(theme.IsLight);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void AnExplicitSetting_IgnoresTheSystem() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current, () => true);
        try
        {
            theme.Apply("dark");
            Assert.False(theme.IsLight);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheWindowAndCards_FollowTheTheme_WithoutRebuilding() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        Themes.Use("light", _ =>
        {
            Assert.Equal(Themes.Brush("WindowBackgroundBrush"), ((SolidColorBrush)fx.Window.Background).Color);
            var card = Ui.Descendants<Border>(fx.Columns.First()).First(b => b.CornerRadius.TopLeft == 10);
            Assert.Equal(Themes.Brush("CardBrush"), ((SolidColorBrush)card.Background).Color);
        });

        Assert.Equal(Themes.Brush("WindowBackgroundBrush"), ((SolidColorBrush)fx.Window.Background).Color);
    });

    [Fact]
    public void ToolbarButtons_UseTheThemedControlStyle() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);

        var darkButton = ((SolidColorBrush)fx.Toolbar.ItalicButton.Background).Color;
        Assert.Equal(Themes.Brush("ControlBrush"), darkButton);

        Themes.Use("light", _ =>
        {
            var lightButton = ((SolidColorBrush)fx.Toolbar.ItalicButton.Background).Color;
            Assert.Equal(Themes.Brush("ControlBrush"), lightButton);
            Assert.NotEqual(darkButton, lightButton);
        });
    });

    [Fact]
    public void DefaultTextColor_FollowsTheTheme_InsideANote() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        var run = fx.Editor.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().First();
        Assert.Equal(Themes.Brush("TextBrush"), ((SolidColorBrush)run.Foreground).Color);

        Themes.Use("light", _ =>
            Assert.Equal(Themes.Brush("TextBrush"), ((SolidColorBrush)run.Foreground).Color));
    });

    [Fact]
    public void AutomaticColor_IsTheAbsenceOfAColor_SoItStillFollowsTheThemeAfterward() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        fx.Toolbar.ApplyTextColor(Colors.Red);

        fx.Toolbar.ApplyTextColor(null);

        Assert.DoesNotContain("color=", fx.Saved());
        Themes.Use("light", _ =>
        {
            var run = fx.Editor.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().First();
            Assert.Equal(Themes.Brush("TextBrush"), ((SolidColorBrush)run.Foreground).Color);
        });
    });

    [Fact]
    public void Highlight_KeepsTextDarkOnItsPastelBackground_AndNoneRestoresAutomatic() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyHighlight(Color.FromRgb(255, 243, 163));
        string highlighted = fx.Saved();
        Assert.Contains("bg=\"#FFF3A3\"", highlighted);
        Assert.Contains("color=\"#1A1A22\"", highlighted);

        fx.Toolbar.ApplyHighlight(null);
        string cleared = fx.Saved();
        Assert.DoesNotContain("bg=", cleared);
        Assert.DoesNotContain("color=", cleared);
    });

    [Fact]
    public void AColorPickedOnPurpose_StaysWhateverTheTheme() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        fx.Toolbar.ApplyTextColor(Colors.Red);

        Themes.Use("light", _ =>
        {
            var run = fx.Editor.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().First();
            Assert.Equal(Colors.Red, ((SolidColorBrush)run.Foreground).Color);
        });
    });

    [Fact]
    public void ClearingAutomaticColorOnPartOfARun_LeavesTheRestAlone() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        fx.Toolbar.ApplyTextColor(Colors.Red);
        var start = fx.Editor.Document.ContentStart.GetPositionAtOffset(3)!;
        fx.Editor.Selection.Select(start, start.GetPositionAtOffset(5)!);

        fx.Toolbar.ApplyTextColor(null);

        string saved = fx.Saved();
        Assert.Contains("color=\"#FF0000\"", saved);
        Assert.Contains("hello world", NoteContent.ToPlainText(saved));
    });
}

public class ConfigReloadTests
{
    private static string Json(string layout = "{}", string animations = "{}", string keybindings = "{}", string theme = "dark") =>
        $$"""{ "theme": "{{theme}}", "layout": {{layout}}, "animations": {{animations}}, "keybindings": {{keybindings}} }""";

    private static (ConfigReloader Reloader, AppConfigStore Store, string Path) Reloader(TempDir dir, WindowFixture fx, string json)
    {
        string path = dir.Combine("config.json");
        File.WriteAllText(path, json);
        var store = new AppConfigStore(path);
        return (new ConfigReloader(store, fx.App, new ThemeService(Application.Current, () => false)), store, path);
    }

    private static NoteRowPanel RowOf(WindowFixture fx) => Ui.Descendants<NoteRowPanel>(fx.Window).First();

    private static void Restore() => new ThemeService(Application.Current, () => false).Apply("dark");

    [Fact]
    public void AValidEdit_ReplacesTheConfig_AndClearsAnyError() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        using var dir = new TempDir();
        var (reloader, _, path) = Reloader(dir, fx, Json("""{ "gapPx": 30 }"""));
        try
        {
            fx.App.ConfigError = "old problem";

            reloader.Reload();

            Assert.Equal(30, fx.App.Config.Layout.GapPx);
            Assert.Null(fx.App.ConfigError);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void ABrokenEdit_KeepsTheRunningSettings_ReportsWhy_AndLeavesTheFileAlone() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        using var dir = new TempDir();
        var (reloader, _, path) = Reloader(dir, fx, Json("""{ "gapPx": 30 }"""));
        try
        {
            reloader.Reload();
            const string halfTyped = """{ "layout": { "gapPx": """;
            File.WriteAllText(path, halfTyped);

            reloader.Reload();

            Assert.Equal(30, fx.App.Config.Layout.GapPx);
            Assert.StartsWith("config.json:", fx.App.ConfigError);
            Assert.Equal(halfTyped, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*"));
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void TheErrorIndicator_AppearsForABrokenEdit_AndGoesAwayWhenFixed() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        using var dir = new TempDir();
        var (reloader, _, path) = Reloader(dir, fx, Json());
        try
        {
            Assert.Equal(Visibility.Collapsed, fx.Window.ConfigProblem.Visibility);

            File.WriteAllText(path, "{ nope");
            reloader.Reload();
            Ui.Settle();
            Assert.Equal(Visibility.Visible, fx.Window.ConfigProblem.Visibility);
            Assert.Contains("not valid JSON", (string)fx.Window.ConfigProblem.ToolTip);

            File.WriteAllText(path, Json());
            reloader.Reload();
            Ui.Settle();
            Assert.Equal(Visibility.Collapsed, fx.Window.ConfigProblem.Visibility);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void ANewGap_ReflowsTheColumnsImmediately() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(new AppConfig { Animations = new AnimationConfig { Enabled = false } }, ("W", 2));
        using var dir = new TempDir();
        var (reloader, _, _) = Reloader(dir, fx, Json("""{ "gapPx": 40 }""", """{ "enabled": false }"""));
        try
        {
            reloader.Reload();
            Ui.Settle();

            var view = fx.Columns.First();
            var row = RowOf(fx);
            Assert.Equal(40, view.TranslatePoint(new Point(0, 0), row).X, 1);
            Assert.Equal(RowLayout.ColumnWidth(0.5, row.ActualWidth, 40), view.ActualWidth, 1);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void CenteredFocus_TakesEffectWithoutARestart() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(new AppConfig { Animations = new AnimationConfig { Enabled = false } }, ("W", 3));
        using var dir = new TempDir();
        var (reloader, _, _) = Reloader(dir, fx, Json("""{ "centerFocusedColumn": true }""", """{ "enabled": false }"""));
        try
        {
            var view = fx.Columns.First();
            var row = RowOf(fx);
            double before = view.TranslatePoint(new Point(0, 0), row).X;

            reloader.Reload();
            Ui.Settle();

            double after = view.TranslatePoint(new Point(0, 0), row).X;
            Assert.Equal(16, before, 1);
            Assert.Equal((row.ActualWidth - view.ActualWidth) / 2, after, 1);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void TurningAnimationsOff_MakesWorkspaceSwitchesInstant() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("A", 1), ("B", 1));
        using var dir = new TempDir();
        var (reloader, _, _) = Reloader(dir, fx, Json(animations: """{ "enabled": false }"""));
        try
        {
            reloader.Reload();
            var strip = Ui.Descendants<WorkspaceStripPanel>(fx.Window).Single();

            fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[1]);
            Ui.Settle();

            Assert.Equal(1, strip.ScrollOffset, 6);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void ARebindingEdit_ReplacesTheShortcut_AndDropsTheOldOne() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        using var dir = new TempDir();
        var (reloader, _, _) = Reloader(dir, fx, Json(keybindings: """{ "newNote": "Ctrl+T" }"""));
        try
        {
            var before = fx.Window.InputBindings.OfType<KeyBinding>().ToList();
            Assert.Contains(before, b => b.Key == Key.N && b.Modifiers == ModifierKeys.Alt);

            reloader.Reload();

            var after = fx.Window.InputBindings.OfType<KeyBinding>().ToList();
            Assert.Contains(after, b => b.Key == Key.T && b.Modifiers == ModifierKeys.Control && ReferenceEquals(b.Command, fx.App.NewNoteCommand));
            Assert.DoesNotContain(after, b => b.Key == Key.N && b.Modifiers == ModifierKeys.Alt);
            Assert.Equal(before.Count, after.Count);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void RepeatedReloads_DoNotPileUpBindings() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        using var dir = new TempDir();
        var (reloader, _, _) = Reloader(dir, fx, Json());
        try
        {
            int baseline = fx.Window.InputBindings.Count;

            for (int i = 0; i < 5; i++) reloader.Reload();

            Assert.Equal(baseline, fx.Window.InputBindings.Count);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void AnInvalidGestureInTheEdit_FallsBackToItsDefault() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        using var dir = new TempDir();
        var (reloader, _, _) = Reloader(dir, fx, Json(keybindings: """{ "newNote": "Banana" }"""));
        try
        {
            reloader.Reload();

            Assert.Contains(fx.Window.InputBindings.OfType<KeyBinding>(), b => b.Key == Key.N && b.Modifiers == ModifierKeys.Alt);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void ThemeEdits_SwitchThePaletteLive() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        using var dir = new TempDir();
        var (reloader, _, path) = Reloader(dir, fx, Json(theme: "light"));
        try
        {
            var darkBackground = ((SolidColorBrush)fx.Window.Background).Color;

            reloader.Reload();
            Ui.Settle();
            var lightBackground = ((SolidColorBrush)fx.Window.Background).Color;

            File.WriteAllText(path, Json(theme: "dark"));
            reloader.Reload();
            Ui.Settle();

            Assert.NotEqual(darkBackground, lightBackground);
            Assert.Equal(darkBackground, ((SolidColorBrush)fx.Window.Background).Color);
        }
        finally { reloader.Dispose(); Restore(); }
    });

    [Fact]
    public void SavingTheFile_IsPickedUpByTheWatcher() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, Json());
        var reloader = new ConfigReloader(new AppConfigStore(path), fx.App, new ThemeService(Application.Current, () => false), fx.Window.Dispatcher);
        try
        {
            Ream.Persistence.Io.AtomicFile.WriteAllText(path, Json("""{ "gapPx": 33 }"""));

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (fx.App.Config.Layout.GapPx != 33 && DateTime.UtcNow < deadline)
            {
                Ui.Settle();
                Thread.Sleep(50);
            }

            Assert.Equal(33, fx.App.Config.Layout.GapPx);
        }
        finally { reloader.Dispose(); Restore(); }
    });
}

public class LazyLoadingTests
{
    private static AppConfig NoAnimation => new() { Animations = new AnimationConfig { Enabled = false } };

    private const string Body = """<ReamNote schemaVersion="1"><Doc><P><R b="1">note text</R></P></Doc></ReamNote>""";

    private static void SettleLoads()
    {
        Ui.Settle();
        Ui.Settle();
    }

    [Fact]
    public void OnlyNotesNearTheViewport_ParseTheirText() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("W", 24));
        var notes = fx.App.CurrentWorkspace.Notes;
        foreach (var note in notes) note.WidthFraction = 0.3;
        SettleLoads();

        int loaded = fx.Columns.Count(c => c.IsContentLoaded);

        Assert.InRange(loaded, 1, 10);
        Assert.True(fx.ColumnOf(notes[0]).IsContentLoaded);
        Assert.False(fx.ColumnOf(notes[^1]).IsContentLoaded);
    });

    [Fact]
    public void FocusingAFarNote_ScrollsToItAndLoadsIt() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("W", 24));
        var notes = fx.App.CurrentWorkspace.Notes;
        foreach (var note in notes) note.WidthFraction = 0.3;
        SettleLoads();

        fx.App.CurrentWorkspace.SetFocus(23);
        SettleLoads();

        Assert.True(fx.ColumnOf(notes[23]).IsContentLoaded);
    });

    [Fact]
    public void LoadedNotes_StayLoaded_WhenTheViewMovesOn() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("W", 24));
        var notes = fx.App.CurrentWorkspace.Notes;
        foreach (var note in notes) note.WidthFraction = 0.3;
        SettleLoads();

        fx.App.CurrentWorkspace.SetFocus(23);
        SettleLoads();

        Assert.True(fx.ColumnOf(notes[0]).IsContentLoaded);
    });

    [Fact]
    public void AFarWorkspace_LoadsNothing_UntilYouGoThere() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("A", 2), ("B", 2), ("C", 2), ("D", 2));
        SettleLoads();

        var adjacent = fx.App.Workspaces[1].Notes.Select(fx.ColumnOf).ToList();
        var far = fx.App.Workspaces[3].Notes.Select(fx.ColumnOf).ToList();
        Assert.All(adjacent, c => Assert.True(c.IsContentLoaded));
        Assert.All(far, c => Assert.False(c.IsContentLoaded));

        fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[3]);
        SettleLoads();

        Assert.All(far, c => Assert.True(c.IsContentLoaded));
    });

    [Fact]
    public void AskingForFocusOnAnUnloadedNote_LoadsItFirst() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("A", 1), ("B", 1), ("C", 1), ("D", 1));
        SettleLoads();
        var far = fx.ColumnOf(fx.App.Workspaces[3].Notes[0]);
        Assert.False(far.IsContentLoaded);

        fx.App.Workspaces[3].Notes[0].RequestEditorFocus();

        Assert.True(far.IsContentLoaded);
    });

    [Fact]
    public void NotesThatWereNeverLoaded_AreSavedExactlyAsTheyWere() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("W", 24));
        var notes = fx.App.CurrentWorkspace.Notes;
        foreach (var note in notes) { note.WidthFraction = 0.3; note.Body = Body; }
        SettleLoads();
        Assert.False(fx.ColumnOf(notes[^1]).IsContentLoaded);

        var saved = SnapshotMapper.ToSnapshot(fx.App).Workspaces[0].Notes;

        Assert.All(saved, n => Assert.Equal(Body, n.Body));
    });

    [Fact]
    public void LoadingANote_IsNotAnEdit() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("W", 24));
        var notes = fx.App.CurrentWorkspace.Notes;
        foreach (var note in notes) { note.WidthFraction = 0.3; note.Body = Body; }
        SettleLoads();
        fx.App.CurrentWorkspace.SetFocus(23);
        SettleLoads();

        var saved = SnapshotMapper.ToSnapshot(fx.App).Workspaces[0].Notes;

        Assert.All(saved, n => Assert.Equal(Body, n.Body));
    });

    [Fact]
    public void PastingIntoAnUnloadedNote_LoadsItSoNothingIsOverwritten() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("A", 1), ("B", 1), ("C", 1), ("D", 1));
        var note = fx.App.Workspaces[3].Notes[0];
        note.Body = Body;
        SettleLoads();
        var far = fx.ColumnOf(note);
        Assert.False(far.IsContentLoaded);

        var pixels = new byte[16];
        far.InsertImage(Ream.Persistence.NoteFormat.NoteImage.EncodePng(
            System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8)));

        Assert.True(far.IsContentLoaded);
        Assert.Contains("note text", NoteContent.ToPlainText(SnapshotMapper.ToSnapshot(fx.App).Workspaces[3].Notes[0].Body));
    });
}
