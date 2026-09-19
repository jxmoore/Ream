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
        var theme = new ThemeService(Application.Current);
        try
        {
            theme.Apply(setting);
            Ui.Settle();
            body(theme);
        }
        finally
        {
            new ThemeService(Application.Current).Apply("dark");
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

    private static ResourceDictionary PaletteOf(ThemeInfo theme) =>
        new() { Source = new Uri($"/Ream.App;component/Themes/{theme.FileName}", UriKind.Relative) };

    private static Color ColorOf(ResourceDictionary palette, string key) => ((SolidColorBrush)palette[key]).Color;

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void EveryPalette_DefinesTheSameBrushes() => Ui.Run(() =>
    {
        var expected = PaletteOf(ThemeCatalog.All[0]).Keys.Cast<string>().OrderBy(k => k).ToList();
        Assert.True(expected.Count >= 20);

        foreach (var theme in ThemeCatalog.All)
            Assert.Equal(expected, PaletteOf(theme).Keys.Cast<string>().OrderBy(k => k).ToList());
    });

    [Fact]
    public void ThePalettes_ActuallyDiffer() => Ui.Run(() =>
    {
        var dark = new ResourceDictionary { Source = Dark };
        var light = new ResourceDictionary { Source = Light };

        foreach (var key in new[] { "WindowBackgroundBrush", "CardBrush", "TextBrush", "ControlBrush" })
            Assert.NotEqual(((SolidColorBrush)dark[key]).Color, ((SolidColorBrush)light[key]).Color);

        var canvases = ThemeCatalog.All.Select(t => ColorOf(PaletteOf(t), "WindowBackgroundBrush")).Distinct().Count();
        Assert.Equal(ThemeCatalog.All.Count, canvases);
    });

    [Fact]
    public void EveryPalette_IsReadable() => Ui.Run(() =>
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var p = PaletteOf(theme);
            void Needs(string text, string background, double ratio) =>
                Assert.True(
                    Contrast(ColorOf(p, text), ColorOf(p, background)) >= ratio,
                    $"{theme.Name}: {text} on {background} is only {Contrast(ColorOf(p, text), ColorOf(p, background)):0.0}:1 (needs {ratio})");

            Needs("TextBrush", "CardBrush", 7);
            Needs("MutedTextBrush", "CardBrush", 4.5);
            Needs("ToolbarMutedBrush", "ToolbarBrush", 4.5);
            Needs("ControlTextBrush", "ControlBrush", 7);
            Needs("ControlTextBrush", "InputBrush", 7);
            Needs("ChipCurrentTextBrush", "ChipCurrentBrush", 4.5);
            Needs("ErrorBrush", "ToolbarBrush", 4.5);
            Needs("AccentBrush", "CardBrush", 3);
        }
    });

    [Fact]
    public void Applying_SwapsThePalette_AndRaisesChangedOnlyOnRealChanges() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current);
        int changes = 0;
        theme.Changed += () => changes++;
        try
        {
            theme.Apply("light");
            var lightCard = Themes.Brush("CardBrush");
            Assert.Equal(1, changes);

            theme.Apply("light");
            Assert.Equal(1, changes);

            theme.Apply("dark");
            var darkCard = Themes.Brush("CardBrush");
            Assert.Equal(2, changes);
            Assert.NotEqual(lightCard, darkCard);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("dracula")]
    [InlineData("catppuccin")]
    [InlineData("material")]
    [InlineData("nord")]
    [InlineData("gruvbox")]
    public void EachTheme_ApplyingItPutsItsPaletteInUse(string id) => Ui.Run(() =>
    {
        var info = ThemeCatalog.Resolve(id);
        Assert.Equal(id, info.Id);

        Themes.Use(id, theme =>
        {
            var palette = PaletteOf(info);
            foreach (var key in new[] { "CardBrush", "WindowBackgroundBrush", "AccentBrush", "TextBrush" })
                Assert.Equal(ColorOf(palette, key), Themes.Brush(key));

            Assert.Equal(id, theme.ThemeId);
            Assert.Equal(info.IsLight, theme.IsLight);
        });
    });

    [Theory]
    [InlineData("system")]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public void UnknownThemes_AndTheRetiredSystemSetting_FallBackToDark(string? setting) => Ui.Run(() =>
    {
        Themes.Use("light", theme =>
        {
            theme.Apply(setting);

            Assert.Equal("dark", theme.ThemeId);
            Assert.False(theme.IsLight);
            Assert.Equal(ColorOf(PaletteOf(ThemeCatalog.Resolve("dark")), "CardBrush"), Themes.Brush("CardBrush"));
        });
    });

    // ----- The canvas and the focus border -----

    [Fact]
    public void TheCanvas_IsSolidByDefault_AndFollowsThePalette() => Ui.Run(() =>
    {
        Themes.Use("nord", _ =>
        {
            Assert.Equal(Themes.Brush("WindowBackgroundBrush"), Themes.Brush(ThemeService.CanvasBrushKey));
            Assert.Equal(255, Themes.Brush(ThemeService.CanvasBrushKey).A);
        });
    });

    [Fact]
    public void TheCanvas_FadesWithOpacity_OnlyWhereBlurIsAvailable() => Ui.Run(() =>
    {
        var supported = new ThemeService(Application.Current, () => true);
        var unsupported = new ThemeService(Application.Current, () => false);
        try
        {
            supported.Apply("dark", canvasOpacity: 40);
            Assert.Equal(102, Themes.Brush(ThemeService.CanvasBrushKey).A);
            Assert.Equal(Themes.Brush("WindowBackgroundBrush").R, Themes.Brush(ThemeService.CanvasBrushKey).R);
            Assert.True(supported.Appearance.SeeThrough);

            supported.Apply("dark", canvasOpacity: 40, canvasBlur: false);
            Assert.Equal(255, Themes.Brush(ThemeService.CanvasBrushKey).A);
            Assert.False(supported.Appearance.SeeThrough);

            unsupported.Apply("dark", canvasOpacity: 40);
            Assert.Equal(255, Themes.Brush(ThemeService.CanvasBrushKey).A);
            Assert.False(unsupported.Appearance.SeeThrough);

            supported.Apply("dark", canvasOpacity: 0);
            Assert.Equal(0, Themes.Brush(ThemeService.CanvasBrushKey).A);

            supported.Apply("dark", canvasOpacity: 100);
            Assert.Equal(255, Themes.Brush(ThemeService.CanvasBrushKey).A);
            Assert.False(supported.Appearance.SeeThrough);
        }
        finally
        {
            unsupported.Apply("dark");
        }
    });

    [Fact]
    public void ChangingOnlyTheOpacity_StillRaisesChanged() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current, () => true);
        try
        {
            theme.Apply("dark");
            int changes = 0;
            theme.Changed += () => changes++;

            theme.Apply("dark", canvasOpacity: 70);
            theme.Apply("dark", canvasOpacity: 70);

            Assert.Equal(1, changes);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheFocusBorder_IsTheThemeAccentUnlessTheConfigSaysOtherwise() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current);
        try
        {
            theme.Apply("dracula");
            Assert.Equal(Themes.Brush("AccentBrush"), Themes.Brush(ThemeService.FocusBorderBrushKey));

            theme.Apply("dracula", focusBorderColor: "#ff8800");
            Assert.Equal(Color.FromRgb(0xff, 0x88, 0x00), Themes.Brush(ThemeService.FocusBorderBrushKey));

            theme.Apply("nord", focusBorderColor: "#80ff8800");
            Assert.Equal(Color.FromArgb(0x80, 0xff, 0x88, 0x00), Themes.Brush(ThemeService.FocusBorderBrushKey));

            theme.Apply("nord", focusBorderColor: "not a color");
            Assert.Equal(Themes.Brush("AccentBrush"), Themes.Brush(ThemeService.FocusBorderBrushKey));

            theme.Apply("gruvbox");
            Assert.Equal(Themes.Brush("AccentBrush"), Themes.Brush(ThemeService.FocusBorderBrushKey));
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheFocusedNotesBorder_UsesTheFocusBorderColor() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var theme = new ThemeService(Application.Current);
        try
        {
            theme.Apply("dark", focusBorderColor: "#00ff00");
            Ui.Settle();

            var focused = Ui.Descendants<Border>(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0])).First(b => b.CornerRadius.TopLeft == 10);
            var other = Ui.Descendants<Border>(fx.ColumnOf(fx.App.CurrentWorkspace.Notes[1])).First(b => b.CornerRadius.TopLeft == 10);

            Assert.Equal(Color.FromRgb(0, 255, 0), ((SolidColorBrush)focused.BorderBrush).Color);
            Assert.Equal(Themes.Brush("CardBorderBrush"), ((SolidColorBrush)other.BorderBrush).Color);
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
        return (new ConfigReloader(store, fx.App, new ThemeService(Application.Current)), store, path);
    }

    private static NoteRowPanel RowOf(WindowFixture fx) => Ui.Descendants<NoteRowPanel>(fx.Window).First();

    private static void Restore() => new ThemeService(Application.Current).Apply("dark");

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
        var (reloader, _, _) = Reloader(dir, fx, Json("""{ "gapPx": 40, "centerFocusedColumn": false }""", """{ "enabled": false }"""));
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
        using var fx = new WindowFixture(new AppConfig { Layout = new LayoutConfig { CenterFocusedColumn = false }, Animations = new AnimationConfig { Enabled = false } }, ("W", 3));
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
        var reloader = new ConfigReloader(new AppConfigStore(path), fx.App, new ThemeService(Application.Current), fx.Window.Dispatcher);
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
        var far = fx.App.Workspaces[4].Notes.Select(fx.ColumnOf).ToList();
        Assert.All(adjacent, c => Assert.True(c.IsContentLoaded));
        Assert.All(far, c => Assert.False(c.IsContentLoaded));

        fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[4]);
        SettleLoads();

        Assert.All(far, c => Assert.True(c.IsContentLoaded));
    });

    [Fact]
    public void AskingForFocusOnAnUnloadedNote_LoadsItFirst() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(NoAnimation, ("A", 1), ("B", 1), ("C", 1), ("D", 1));
        SettleLoads();
        var far = fx.ColumnOf(fx.App.Workspaces[4].Notes[0]);
        Assert.False(far.IsContentLoaded);

        fx.App.Workspaces[4].Notes[0].RequestEditorFocus();

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
        var note = fx.App.Workspaces[4].Notes[0];
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
