using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class SettingsViewModelTests
{
    private sealed class Rig : IDisposable
    {
        private readonly TempDir _dir = new();

        public Rig(AppConfig? config = null, bool supported = true, bool withStore = true, string? fileText = null)
        {
            App = new AppViewModel(config ?? new AppConfig(), []);
            Theme = new ThemeService(Application.Current, () => supported);
            Path = _dir.Combine("config.json");
            if (fileText is not null) File.WriteAllText(Path, fileText);
            else File.WriteAllText(Path, """{ "theme": "dark", "keybindings": { "newNote": "Ctrl+T" } }""");

            Store = withStore ? new AppConfigStore(Path) : null;
            Settings = new SettingsViewModel(App, Theme, Store, dispatcher: null, blurSupported: () => supported);
            Theme.Apply(App.Config);
        }

        public AppViewModel App { get; }
        public ThemeService Theme { get; }
        public AppConfigStore? Store { get; }
        public SettingsViewModel Settings { get; }
        public string Path { get; }

        public JsonObject Saved => JsonNode.Parse(File.ReadAllText(Path))!.AsObject();

        public void Dispose()
        {
            Settings.Dispose();
            Theme.Apply("dark");
            _dir.Dispose();
        }
    }

    [Fact]
    public void ItListsEveryTheme_WithTheCurrentOneSelected() => Ui.Run(() =>
    {
        using var rig = new Rig(new AppConfig { Theme = "nord" });

        Assert.Equal(ThemeCatalog.All.Select(t => t.Id), rig.Settings.Themes.Select(t => t.Id));
        Assert.Equal(ThemeCatalog.All.Select(t => t.Name), rig.Settings.Themes.Select(t => t.Name));
        Assert.Equal(["nord"], rig.Settings.Themes.Where(t => t.IsSelected).Select(t => t.Id));
        Assert.Equal("nord", rig.Settings.SelectedThemeId);
    });

    [Fact]
    public void EachSwatch_ShowsItsThemesOwnColors() => Ui.Run(() =>
    {
        using var rig = new Rig();
        var canvases = rig.Settings.Themes.Select(t => ((SolidColorBrush)t.Canvas).Color).Distinct().Count();
        var accents = rig.Settings.Themes.Select(t => ((SolidColorBrush)t.Accent).Color).Distinct().Count();

        Assert.Equal(ThemeCatalog.All.Count, canvases);
        Assert.Equal(ThemeCatalog.All.Count, accents);
        Assert.All(rig.Settings.Themes, t => Assert.True(t.Canvas.IsFrozen && t.Card.IsFrozen && t.Accent.IsFrozen));
    });

    [Fact]
    public void ChoosingATheme_AppliesItAtOnce_AndRecordsItInTheConfig() => Ui.Run(() =>
    {
        using var rig = new Rig();
        var dracula = rig.Settings.Themes.Single(t => t.Id == "dracula");

        rig.Settings.SelectThemeCommand.Execute(dracula);

        Assert.Equal("dracula", rig.App.Config.Theme);
        Assert.Equal("dracula", rig.Theme.ThemeId);
        Assert.Equal(((SolidColorBrush)dracula.Card).Color, Themes.Brush("CardBrush"));
        Assert.Equal(["dracula"], rig.Settings.Themes.Where(t => t.IsSelected).Select(t => t.Id));
        Assert.Equal("dracula", rig.Settings.SelectedThemeId);
    });

    [Fact]
    public void ChoosingTheThemeAlreadyInUse_DoesNothing() => Ui.Run(() =>
    {
        using var rig = new Rig();
        var before = rig.App.Config;

        rig.Settings.SelectThemeCommand.Execute(rig.Settings.Themes.Single(t => t.Id == "dark"));
        rig.Settings.SelectThemeCommand.Execute(null);

        Assert.Same(before, rig.App.Config);
    });

    [Fact]
    public void TheSlider_MovesTheCanvasOpacity_AndLabelsIt() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.OpacityPercent = 40;

        Assert.Equal(40, rig.App.Config.CanvasOpacity);
        Assert.Equal("40%", rig.Settings.OpacityLabel);
        Assert.Equal(102, Themes.Brush(ThemeService.CanvasBrushKey).A);
    });

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-20, 0)]
    public void Opacity_IsKeptBetweenZeroAndAHundred(int typed, int expected) => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.OpacityPercent = typed;

        Assert.Equal(expected, rig.Settings.OpacityPercent);
        Assert.Equal(expected, rig.App.Config.CanvasOpacity);
    });

    [Fact]
    public void ChangingOneSetting_KeepsTheOther() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.OpacityPercent = 55;
        rig.Settings.SelectThemeCommand.Execute(rig.Settings.Themes.Single(t => t.Id == "gruvbox"));

        Assert.Equal(55, rig.App.Config.CanvasOpacity);
        Assert.Equal("gruvbox", rig.App.Config.Theme);
    });

    [Fact]
    public void TheNoteSlider_MovesTheNoteOpacity_LabelsIt_AndLeavesTheCanvasAlone() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.NoteOpacityPercent = 35;

        Assert.Equal(35, rig.App.Config.NoteOpacity);
        Assert.Equal("35%", rig.Settings.NoteOpacityLabel);
        Assert.Equal(100, rig.App.Config.CanvasOpacity);
        Assert.Equal(89, Themes.Brush(ThemeService.NoteBrushKey).A);
    });

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-20, 0)]
    public void NoteOpacity_IsKeptBetweenZeroAndAHundred(int typed, int expected) => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.NoteOpacityPercent = typed;

        Assert.Equal(expected, rig.Settings.NoteOpacityPercent);
        Assert.Equal(expected, rig.App.Config.NoteOpacity);
    });

    [Fact]
    public void NoteOpacity_IsSavedToTheConfigFile_WithoutDisturbingTheRest() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.NoteOpacityPercent = 45;
        rig.Settings.Flush();

        var saved = rig.Saved;
        Assert.Equal(45, (int?)saved["noteOpacity"]);
        Assert.Equal("Ctrl+T", (string?)saved["keybindings"]!["newNote"]);
        Assert.Null(saved["canvasOpacity"]);
    });

    [Fact]
    public void NoteOpacityEditedInTheFile_IsFollowedByTheSlider() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.App.Config = rig.App.Config.With(noteOpacity: 20);

        Assert.Equal(20, rig.Settings.NoteOpacityPercent);
    });

    [Fact]
    public void TheZoomSetting_ChangesTheConfig_AndLabelsIt() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.ZoomPercent = 150;

        Assert.Equal(150, rig.App.Config.Zoom);
        Assert.Equal("150%", rig.Settings.ZoomLabel);
        Assert.Equal(1.5, Themes.Resource<double>(ThemeService.NoteZoomScaleKey));
    });

    [Theory]
    [InlineData(500, 200)]
    [InlineData(10, 50)]
    public void Zoom_IsKeptBetweenFiftyAndTwoHundred(int typed, int expected) => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.ZoomPercent = typed;

        Assert.Equal(expected, rig.Settings.ZoomPercent);
        Assert.Equal(expected, rig.App.Config.Zoom);
    });

    [Fact]
    public void Zoom_IsSavedToTheConfigFile_WithoutDisturbingTheRest() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.ZoomPercent = 80;
        rig.Settings.Flush();

        var saved = rig.Saved;
        Assert.Equal(80, (int?)saved["zoom"]);
        Assert.Equal("Ctrl+T", (string?)saved["keybindings"]!["newNote"]);
        Assert.Null(saved["canvasOpacity"]);
    });

    [Fact]
    public void ZoomEditedInTheFile_IsFollowedBy_ThePanel() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.App.Config = rig.App.Config.With(zoom: 60);

        Assert.Equal(60, rig.Settings.ZoomPercent);
    });

    [Fact]
    public void TheGapSetting_ChangesTheLayoutConfig_AndSavesUnderLayout() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.GapPx = 40;
        rig.Settings.Flush();

        Assert.Equal(40, rig.App.Config.Layout.GapPx);
        var saved = rig.Saved;
        Assert.Equal(40, (double?)saved["layout"]!["gapPx"]);
    });

    [Theory]
    [InlineData(500, 60)]
    [InlineData(-10, 8)]
    public void Gap_IsKeptBetweenEightAndSixty(double typed, double expected) => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.GapPx = typed;

        Assert.Equal(expected, rig.Settings.GapPx);
        Assert.Equal(expected, rig.App.Config.Layout.GapPx);
    });

    [Fact]
    public void TheCenterFocusedColumnToggle_ChangesTheLayoutConfig_AndKeepsTheGap() => Ui.Run(() =>
    {
        using var rig = new Rig(new AppConfig { Layout = new LayoutConfig { GapPx = 40, CenterFocusedColumn = true } });

        rig.Settings.CenterFocusedColumn = false;
        rig.Settings.Flush();

        Assert.False(rig.App.Config.Layout.CenterFocusedColumn);
        Assert.Equal(40, rig.App.Config.Layout.GapPx); // untouched
        var saved = rig.Saved;
        Assert.Equal(false, (bool?)saved["layout"]!["centerFocusedColumn"]);
    });

    [Fact]
    public void LayoutChanges_DoNotDisturbOtherLayoutKeysAlreadyInTheFile() => Ui.Run(() =>
    {
        using var rig = new Rig(fileText: """
            { "theme": "dark", "layout": { "gapPx": 28, "focusFirstNoteOnSwitch": false } }
            """);

        rig.Settings.GapPx = 12;
        rig.Settings.Flush();

        var layout = rig.Saved["layout"]!;
        Assert.Equal(12, (double?)layout["gapPx"]);
        Assert.Equal(false, (bool?)layout["focusFirstNoteOnSwitch"]); // carried over, untouched
    });

    [Fact]
    public void OtherConfigSettings_AreCarriedAlong() => Ui.Run(() =>
    {
        var config = new AppConfig { Layout = new LayoutConfig { GapPx = 30 }, CanvasBlur = false };
        config.Keybindings["newNote"] = "Ctrl+T";
        using var rig = new Rig(config);

        rig.Settings.OpacityPercent = 70;

        Assert.Equal(30, rig.App.Config.Layout.GapPx);
        Assert.False(rig.App.Config.CanvasBlur);
        Assert.Equal("Ctrl+T", rig.App.Config.Keybindings["newNote"]);
    });

    // ----- Saving -----

    [Fact]
    public void Changes_AreWrittenToTheFile_WhenTheyAreFlushed_ChangingOnlyTheirKeys() => Ui.Run(() =>
    {
        using var rig = new Rig();

        rig.Settings.SelectThemeCommand.Execute(rig.Settings.Themes.Single(t => t.Id == "nord"));
        rig.Settings.OpacityPercent = 35;
        Assert.Equal("dark", (string?)rig.Saved["theme"]);

        rig.Settings.Flush();

        var saved = rig.Saved;
        Assert.Equal("nord", (string?)saved["theme"]);
        Assert.Equal(35, (int?)saved["canvasOpacity"]);
        Assert.Equal("Ctrl+T", (string?)saved["keybindings"]!["newNote"]);
    });

    [Fact]
    public void ADragThroughManyValues_SavesTheLastOne() => Ui.Run(() =>
    {
        using var rig = new Rig();

        for (int value = 100; value >= 20; value--) rig.Settings.OpacityPercent = value;
        rig.Settings.Flush();

        Assert.Equal(20, (int?)rig.Saved["canvasOpacity"]);
    });

    [Fact]
    public void NothingIsWritten_WhenNothingChanged() => Ui.Run(() =>
    {
        using var rig = new Rig();
        string before = File.ReadAllText(rig.Path);

        rig.Settings.Flush();

        Assert.Equal(before, File.ReadAllText(rig.Path));
    });

    [Fact]
    public void WithoutAStore_ChangesStillApply_ForThisRun() => Ui.Run(() =>
    {
        using var rig = new Rig(withStore: false);

        rig.Settings.OpacityPercent = 45;
        rig.Settings.Flush();

        Assert.Equal(45, rig.App.Config.CanvasOpacity);
        Assert.Null(rig.App.ConfigError);
    });

    [Fact]
    public void AFileThatCantBeRewritten_IsReportedAndLeftAlone() => Ui.Run(() =>
    {
        string original = "{\n  // keep me\n  \"theme\": \"dark\"\n}";
        using var rig = new Rig(fileText: original);

        rig.Settings.OpacityPercent = 50;
        rig.Settings.Flush();

        Assert.Contains("Settings not saved", rig.App.ConfigError);
        Assert.Equal(original, File.ReadAllText(rig.Path));
        Assert.Equal(50, rig.App.Config.CanvasOpacity);
    });

    // ----- Following the config -----

    [Fact]
    public void WhenTheConfigIsReloaded_ThePanelFollowsIt_WithoutSavingAnything() => Ui.Run(() =>
    {
        using var rig = new Rig();
        string before = File.ReadAllText(rig.Path);

        rig.App.Config = new AppConfig { Theme = "material", CanvasOpacity = 25 };
        rig.Settings.Flush();

        Assert.Equal("material", rig.Settings.SelectedThemeId);
        Assert.Equal(25, rig.Settings.OpacityPercent);
        Assert.Equal(["material"], rig.Settings.Themes.Where(t => t.IsSelected).Select(t => t.Id));
        Assert.Equal(before, File.ReadAllText(rig.Path));
    });

    [Fact]
    public void ARetiredOrUnknownThemeInTheConfig_ShowsDarkAsSelected() => Ui.Run(() =>
    {
        using var rig = new Rig(new AppConfig { Theme = "system" });

        Assert.Equal("dark", rig.Settings.SelectedThemeId);
        Assert.Equal(["dark"], rig.Settings.Themes.Where(t => t.IsSelected).Select(t => t.Id));
    });

    // ----- What the slider says about blur -----

    [Fact]
    public void WithBlurOn_TheHintSaysTheDesktopIsBlurred() => Ui.Run(() =>
    {
        using var rig = new Rig();

        Assert.Contains("blurred", rig.Settings.OpacityHint);
    });

    [Fact]
    public void WithBlurTurnedOffInConfig_TheHintSaysSo_ButOpacityStillWorks() => Ui.Run(() =>
    {
        using var rig = new Rig(new AppConfig { CanvasBlur = false });

        Assert.Contains("canvasBlur", rig.Settings.OpacityHint);
        rig.Settings.OpacityPercent = 30;
        Assert.Equal(30, rig.App.Config.CanvasOpacity);
        Assert.Equal(77, Themes.Brush(ThemeService.CanvasBrushKey).A);
    });

    [Fact]
    public void OnWindowsWithoutBlur_OpacityStillWorks_AndTheHintSaysWhatIsMissing() => Ui.Run(() =>
    {
        using var rig = new Rig(supported: false);

        Assert.Contains("Windows 10", rig.Settings.OpacityHint);
        rig.Settings.OpacityPercent = 30;
        Assert.Equal(77, Themes.Brush(ThemeService.CanvasBrushKey).A);
    });
    // ----- Our own save coming back through the file watcher -----

    [Fact]
    public void TheReloader_IgnoresTheFileChangeCausedByOurOwnSave_ButNotAnEditByHand() => Ui.Run(() =>
    {
        using var rig = new Rig();
        var reloader = new ConfigReloader(rig.Store!, rig.App, rig.Theme);

        rig.Settings.OpacityPercent = 60;
        rig.Settings.Flush();

        // Meanwhile the slider has moved on; the echo of the earlier save must not drag it back.
        rig.Settings.OpacityPercent = 33;
        reloader.Reload();
        Assert.Equal(33, rig.App.Config.CanvasOpacity);

        // An edit by hand is a different file, and is applied.
        File.WriteAllText(rig.Path, """{ "theme": "nord", "canvasOpacity": 80 }""");
        reloader.Reload();
        Assert.Equal("nord", rig.App.Config.Theme);
        Assert.Equal(80, rig.App.Config.CanvasOpacity);
        Assert.Equal(80, rig.Settings.OpacityPercent);
    });

    [Fact]
    public void EditingBackToTheExactTextWeLastWrote_IsStillApplied() => Ui.Run(() =>
    {
        using var rig = new Rig();
        var reloader = new ConfigReloader(rig.Store!, rig.App, rig.Theme);

        rig.Settings.OpacityPercent = 60;
        rig.Settings.Flush();
        string ours = File.ReadAllText(rig.Path);

        File.WriteAllText(rig.Path, """{ "canvasOpacity": 10 }""");
        reloader.Reload();
        Assert.Equal(10, rig.App.Config.CanvasOpacity);

        File.WriteAllText(rig.Path, ours);
        reloader.Reload();
        Assert.Equal(60, rig.App.Config.CanvasOpacity);
    });
}

public class ViewRibbonTests
{
    private static SettingsViewModel Make(AppViewModel app, ThemeService theme, bool supported = true) =>
        new(app, theme, store: null, dispatcher: null, blurSupported: () => supported);

    private static void Click(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static void RaiseClick(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Slider SliderNamed(ViewRibbonView view, string name) => (Slider)view.FindName(name);

    [Fact]
    public void ThemesAreADropdown_OneEntryPerTheme_AndPickingOnePicksIt() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var box = (ComboBox)view.FindName("ThemeBox");

            Assert.Equal(ThemeCatalog.All.Select(t => t.Name), box.Items.Cast<ThemeOption>().Select(o => o.Name));
            Assert.Equal("dark", ((ThemeOption)box.SelectedItem).Id);

            box.SelectedIndex = 2;
            Ui.Settle();

            Assert.Equal("dracula", app.Config.Theme);
            Assert.Equal("dracula", ((ThemeOption)box.SelectedItem).Id);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheDropdown_FollowsTheConfigWhenItChangesElsewhere() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var box = (ComboBox)view.FindName("ThemeBox");

            app.Config = app.Config.With(theme: "nord");
            Ui.Settle();

            Assert.Equal("nord", ((ThemeOption)box.SelectedItem).Id);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheDropdownShowsTheSelectedThemesSwatchAndName() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig { Theme = "gruvbox" }, []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var box = (ComboBox)view.FindName("ThemeBox");
            Ui.Settle();

            Assert.Contains(Ui.Descendants<TextBlock>(box), t => t.Text == "Gruvbox");
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheTwoSliders_AreStackedAndShort() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var canvas = SliderNamed(view, "OpacitySlider");
            var notes = SliderNamed(view, "NoteOpacitySlider");

            var canvasAt = canvas.TranslatePoint(new Point(0, 0), view);
            var notesAt = notes.TranslatePoint(new Point(0, 0), view);

            Assert.Equal(canvasAt.X, notesAt.X);
            Assert.True(notesAt.Y - canvasAt.Y >= 24, "the Notes slider sits under the Canvas one");
            Assert.True(canvas.ActualWidth <= 130 && notes.ActualWidth <= 130);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheSliders_DriveTheTwoOpacities_AndTheirLabels() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var canvas = SliderNamed(view, "OpacitySlider");
            var notes = SliderNamed(view, "NoteOpacitySlider");
            Assert.Equal(100, canvas.Value);
            Assert.Equal(100, notes.Value);
            Assert.True(canvas.IsEnabled && notes.IsEnabled);

            canvas.Value = 30;
            notes.Value = 60;
            Ui.Settle();

            Assert.Equal(30, app.Config.CanvasOpacity);
            Assert.Equal(60, app.Config.NoteOpacity);
            Assert.Equal("30%", ((TextBlock)view.FindName("CanvasOpacityLabel")).Text);
            Assert.Equal("60%", ((TextBlock)view.FindName("NoteOpacityLabel")).Text);

            app.Config = app.Config.With(canvasOpacity: 75, noteOpacity: 10);
            Ui.Settle();
            Assert.Equal(75, canvas.Value);
            Assert.Equal(10, notes.Value);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheZoomResetTile_LabelsAndTooltipFollowTheZoom_AndAClickResetsIt() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            Assert.Equal("100%", ((TextBlock)view.FindName("ZoomLabel")).Text);
            Assert.Equal("Zoom is 100%", ((Button)view.FindName("ZoomResetButton")).ToolTip);

            settings.ZoomPercent = 125;
            Ui.Settle();
            Assert.Equal("125%", ((TextBlock)view.FindName("ZoomLabel")).Text);
            Assert.Equal("Zoom is 125% - click to reset to 100%", ((Button)view.FindName("ZoomResetButton")).ToolTip);

            RaiseClick((Button)view.FindName("ZoomResetButton"));
            Ui.Settle();
            Assert.Equal(100, app.Config.Zoom);
            Assert.Equal("100%", ((TextBlock)view.FindName("ZoomLabel")).Text);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheZoomTile_OffersEveryPreset_AndPickingOneSetsTheZoom_WithTheCurrentOneChecked() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig { Zoom = 125 }, []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var button = (Button)view.FindName("ZoomButton");

            RaiseClick(button);
            Ui.Settle();

            var menu = button.ContextMenu;
            Assert.NotNull(menu);
            var items = menu.Items.Cast<MenuItem>().ToList();
            Assert.Equal(["50%", "75%", "100%", "125%", "150%", "175%", "200%"], items.Select(i => (string)i.Header));
            Assert.True(items.Single(i => (string)i.Header == "125%").IsChecked);
            Assert.False(items.Single(i => (string)i.Header == "100%").IsChecked);

            items.Single(i => (string)i.Header == "150%").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Ui.Settle();

            Assert.Equal(150, app.Config.Zoom);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheGapSlider_AndTheCenterCheckbox_ChangeTheLayout() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var gap = SliderNamed(view, "GapSlider");
            var center = (CheckBox)view.FindName("CenterFocusedToggle");
            Assert.Equal(28, gap.Value);
            Assert.True(center.IsChecked);

            gap.Value = 16;
            center.IsChecked = false;
            Ui.Settle();

            Assert.Equal(16, app.Config.Layout.GapPx);
            Assert.False(app.Config.Layout.CenterFocusedColumn);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheShowGroup_HasRulerGridlinesAndNavigationPane_PlacedButDisabled() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            foreach (var name in new[] { "RulerCheckBox", "GridlinesCheckBox", "NavigationPaneCheckBox" })
            {
                var box = (CheckBox)view.FindName(name);
                Assert.True(box.IsVisible, name);
                Assert.False(box.IsEnabled, name);
            }
            Assert.Equal("Ruler", ((CheckBox)view.FindName("RulerCheckBox")).Content);
            Assert.Equal("Gridlines", ((CheckBox)view.FindName("GridlinesCheckBox")).Content);
            Assert.Equal("Navigation Pane", ((CheckBox)view.FindName("NavigationPaneCheckBox")).Content);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    /// <summary>Word controls Ream mirrors for the layout but has nothing behind yet - present, visible, and disabled, same convention as the Home tab's own placed-but-disabled controls.</summary>
    private static readonly string[] PlacedButDisabled =
    [
        "PrintLayoutButton", "WebLayoutButton", "OutlineButton", "DraftButton", // Views (Read Mode is real, the rest aren't)
        "VerticalButton", "SideToSideButton", // Page Movement
        "OnePageButton", "MultiplePagesButton", "PageWidthButton", // Zoom's page-view trio
        "SplitButton", "ViewSideBySideButton", "SynchronousScrollingButton", "ResetWindowPositionButton", "SwitchWindowsButton", // Window
    ];

    [Fact]
    public void WordControlsWithNothingBehindThemYet_ArePlaced_ButDisabled() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            foreach (var name in PlacedButDisabled)
            {
                var control = (Control)view.FindName(name);
                Assert.True(control.IsVisible, name);
                Assert.False(control.IsEnabled, name);
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheControlsThatWork_AreEnabled_EvenAmongTheDisabledOnes() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            string[] working =
            [
                "ThemeBox", "OpacitySlider", "NoteOpacitySlider", "GapSlider", "CenterFocusedToggle",
                "ReadModeButton", "ZoomButton", "ZoomResetButton", "NewNoteButton", "NewWorkspaceButton",
            ];
            foreach (var name in working) Assert.True(((Control)view.FindName(name)).IsEnabled, name);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheNewNoteAndNewWorkspaceTiles_RaiseTheirEvents() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            bool note = false, workspace = false;
            view.NewNoteRequested += () => note = true;
            view.NewWorkspaceRequested += () => workspace = true;

            RaiseClick((Button)view.FindName("NewNoteButton"));
            RaiseClick((Button)view.FindName("NewWorkspaceButton"));

            Assert.True(note);
            Assert.True(workspace);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheReadModeButton_RaisesItsEvent() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            bool raised = false;
            view.ReadModeRequested += () => raised = true;

            RaiseClick((Button)view.FindName("ReadModeButton"));

            Assert.True(raised);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void OnWindowsWithoutBlur_TheSlidersStillWork_AndTheTooltipSaysWhatIsMissing() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => false);
        var settings = Make(app, theme, supported: false);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            var canvas = SliderNamed(view, "OpacitySlider");
            Assert.True(canvas.IsEnabled);
            Assert.Contains("Windows 10", (string)canvas.ToolTip);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    /// <summary>Hosts a control in an off-screen window for the length of a test.</summary>
    private sealed class WindowHolder : IDisposable
    {
        private readonly Window _window;

        public WindowHolder(FrameworkElement content)
        {
            _window = Ui.Show(content, 1000, 140);
        }

        public void Dispose() => _window.Close();
    }
}
