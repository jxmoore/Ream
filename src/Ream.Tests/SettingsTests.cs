using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    /// <summary>SetRibbonPinned isn't bound from XAML (MainWindow's pin button calls it directly - the pin itself
    /// lives in RibbonVisibility, not SettingsViewModel), but it goes through the exact same "update in memory, save
    /// shortly after" path as everything the View tab does edit.</summary>
    [Fact]
    public void SetRibbonPinned_ChangesTheRibbonConfig_AndPersistsIt() => Ui.Run(() =>
    {
        using var rig = new Rig(new AppConfig { Ribbon = new RibbonConfig { AutoHide = true, Pinned = false } });

        rig.Settings.SetRibbonPinned(true);
        rig.Settings.Flush();

        Assert.True(rig.App.Config.Ribbon.Pinned);
        Assert.True(rig.App.Config.Ribbon.AutoHide); // untouched
        Assert.Equal(true, (bool?)rig.Saved["ribbon"]!["pinned"]);
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

    /// <summary>The rows of a Switch Workspaces/Switch Notes drop-down (ViewRibbonView.BuildPopupShell/BuildRow) -
    /// a Border per row inside a StackPanel inside the popup's own outer Border.</summary>
    private static IReadOnlyList<Border> MenuRows(Popup popup) =>
        ((StackPanel)((Border)popup.Child).Child).Children.Cast<Border>().ToList();

    private static string RowText(Border row) => ((Grid)row.Child).Children.OfType<TextBlock>().Last().Text;

    private static bool RowIsCurrent(Border row) => ((Grid)row.Child).Children.OfType<TextBlock>().Count() > 1;

    private static void RaiseRowClick(Border row) =>
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });

    [Fact]
    public void TheGroupLabels_AllAlignAtTheSameHeight() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            // The Theme tile's own inner label also happens to read "Theme" - only the RibbonGroupLabel captions
            // (never inside a button) are what this test is checking.
            var labels = Ui.Descendants<TextBlock>(view)
                .Where(t => t.Text is "View" or "Window" or "Show" or "Zoom" or "Theme")
                .Where(t => Ui.Ancestor<ButtonBase>(t) is null)
                .ToList();
            Assert.Equal(5, labels.Count);

            var tops = labels.Select(l => l.TranslatePoint(new Point(0, 0), view).Y).ToList();
            Assert.True(tops.Max() - tops.Min() < 0.5, "label tops: " + string.Join(", ", tops));
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheZoomResetTile_TooltipFollowsTheZoom_AndAClickResetsIt() => Ui.Run(() =>
    {
        // No live percentage printed on the tile itself any more - just the tooltip.
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            Assert.Equal("Zoom is 100%", ((Button)view.FindName("ZoomResetButton")).ToolTip);

            settings.ZoomPercent = 125;
            Ui.Settle();
            Assert.Equal("Zoom is 125% - click to reset to 100%", ((Button)view.FindName("ZoomResetButton")).ToolTip);

            RaiseClick((Button)view.FindName("ZoomResetButton"));
            Ui.Settle();
            Assert.Equal(100, app.Config.Zoom);
            Assert.Equal("Zoom is 100%", ((Button)view.FindName("ZoomResetButton")).ToolTip);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheZoomInAndOutButtons_StepTheZoomByTen_Clamped() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig { Zoom = 195 }, []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            RaiseClick((Button)view.FindName("ZoomInButton"));
            Ui.Settle();
            Assert.Equal(200, settings.ZoomPercent); // clamped at the top, not 205

            RaiseClick((Button)view.FindName("ZoomOutButton"));
            RaiseClick((Button)view.FindName("ZoomOutButton"));
            Ui.Settle();
            Assert.Equal(180, settings.ZoomPercent);
        }
        finally
        {
            theme.Apply("dark");
        }
    });


    [Fact]
    public void TheShowGroup_GridlinesAndNavigationPane_AreRealTwoWayCheckBoxes() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            var gridlines = (CheckBox)view.FindName("GridlinesCheckBox");
            var navPane = (CheckBox)view.FindName("NavigationPaneCheckBox");
            Assert.True(gridlines.IsEnabled);
            Assert.True(navPane.IsEnabled);
            Assert.False(gridlines.IsChecked);
            Assert.False(navPane.IsChecked);

            gridlines.IsChecked = true;
            navPane.IsChecked = true;
            Ui.Settle();

            Assert.True(settings.GridlinesOn);
            Assert.True(settings.NavigationPaneOpen);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    private static (AppViewModel App, NoteViewModel Note) FixtureWithNote()
    {
        var workspace = new WorkspaceViewModel("W");
        var note = new NoteViewModel();
        workspace.LoadNotes([note], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        return (app, note);
    }

    [Fact]
    public void TheRulerCheckBox_TogglesTheFocusedNotesRuler() => Ui.Run(() =>
    {
        var (app, note) = FixtureWithNote();
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var box = (CheckBox)view.FindName("RulerCheckBox");
            Assert.False(box.IsChecked);

            box.IsChecked = true;
            Ui.Settle();

            Assert.True(note.ShowRuler);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheOnePageButton_TogglesOnePageMode() => Ui.Run(() =>
    {
        var (app, note) = FixtureWithNote();
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var toggle = (ToggleButton)view.FindName("OnePageButton");
            Assert.False(toggle.IsChecked);

            toggle.IsChecked = true;
            Ui.Settle();

            Assert.True(app.OnePageMode);
            Assert.False(note.IsHiddenByOnePage); // the only note in the workspace - never hides itself
            Assert.False(note.IsFullscreen); // One Page doesn't enlarge the note that stays, unlike Read Mode
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheThemeButton_RaisesItsEvent() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            bool raised = false;
            view.ThemeRequested += () => raised = true;

            RaiseClick((Button)view.FindName("ThemeButton"));

            Assert.True(raised);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheSwitchWorkspacesButton_ListsWorkspaces_AndPickingOneSwitchesToIt() => Ui.Run(() =>
    {
        var w1 = new WorkspaceViewModel("Alpha");
        w1.LoadNotes([new NoteViewModel()], null);
        var w2 = new WorkspaceViewModel("Beta");
        w2.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [w1, w2], currentIndex: 0);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var button = (Button)view.FindName("SwitchWorkspacesButton");

            RaiseClick(button);
            Ui.Settle();

            var menu = view.SwitchWorkspacesMenu;
            Assert.NotNull(menu);
            var rows = MenuRows(menu);
            Assert.Equal(["Alpha", "Beta"], rows.Select(RowText));
            Assert.True(RowIsCurrent(rows[0]));

            RaiseRowClick(rows[1]);
            Ui.Settle();

            Assert.Same(w2, app.CurrentWorkspace);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheSwitchNotesButton_ListsTheWorkspacesNotes_AndPickingOneFocusesIt() => Ui.Run(() =>
    {
        var workspace = new WorkspaceViewModel("W");
        var first = new NoteViewModel { Title = "First" };
        var second = new NoteViewModel { Title = "Second" };
        workspace.LoadNotes([first, second], first.Id);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var button = (Button)view.FindName("SwitchNotesButton");

            RaiseClick(button);
            Ui.Settle();

            var menu = view.SwitchNotesMenu;
            Assert.NotNull(menu);
            var rows = MenuRows(menu);
            Assert.Equal(["First", "Second"], rows.Select(RowText));
            Assert.True(RowIsCurrent(rows[0]));

            RaiseRowClick(rows[1]);
            Ui.Settle();

            Assert.Same(second, app.CurrentWorkspace.FocusedNote);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void ClickingSwitchNotes_AgainWhileItsMenuIsOpen_ClosesItInsteadOfReopening() => Ui.Run(() =>
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var button = (Button)view.FindName("SwitchNotesButton");

            RaiseClick(button);
            Ui.Settle();
            var firstMenu = view.SwitchNotesMenu;
            Assert.NotNull(firstMenu);
            Assert.True(firstMenu.IsOpen);

            // The drop-down is a plain Popup, closed only by ViewRibbonView's own code (CloseIfOpen here, or
            // OnWindowPreviewMouseDown for a click elsewhere) - never by any WPF menu-dismissal machinery - so
            // this exercises the real production path directly, with no WPF-internal timing to simulate.
            RaiseClick(button);
            Ui.Settle();

            Assert.False(firstMenu.IsOpen); // closed, not left open and hidden behind a freshly-opened second one
            Assert.Null(view.SwitchNotesMenu); // and not tracked as still open, either
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void ClickingSwitchWorkspaces_AgainWhileItsMenuIsOpen_ClosesItInsteadOfReopening() => Ui.Run(() =>
    {
        var w1 = new WorkspaceViewModel("Alpha");
        w1.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [w1], currentIndex: 0);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var button = (Button)view.FindName("SwitchWorkspacesButton");

            RaiseClick(button);
            Ui.Settle();
            var firstMenu = view.SwitchWorkspacesMenu;
            Assert.NotNull(firstMenu);
            Assert.True(firstMenu.IsOpen);

            RaiseClick(button);
            Ui.Settle();

            Assert.False(firstMenu.IsOpen);
            Assert.Null(view.SwitchWorkspacesMenu);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void ClickingElsewhere_ClosesAnOpenSwitchMenu() => Ui.Run(() =>
    {
        // The drop-down is a plain Popup with StaysOpen="True" (so a reclick on its own button can close it
        // deterministically - see ClickingSwitchNotes_AgainWhileItsMenuIsOpen_ClosesItInsteadOfReopening), which
        // means nothing closes it on an outside click automatically: ViewRibbonView reimplements that itself
        // (OnWindowPreviewMouseDown), so this checks it actually does.
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var switchButton = (Button)view.FindName("SwitchNotesButton");
            var elsewhere = (Button)view.FindName("ZoomInButton");

            RaiseClick(switchButton);
            Ui.Settle();
            var menu = view.SwitchNotesMenu;
            Assert.NotNull(menu);
            Assert.True(menu.IsOpen);

            elsewhere.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
            Ui.Settle();

            Assert.False(menu.IsOpen);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void ASwitchMenu_KeepsTheRibbonUp_EvenWhenNotPinned() => Ui.Run(() =>
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            bool? menuOpen = null;
            view.MenuOpenChanged += () => menuOpen = view.IsMenuOpen;

            RaiseClick((Button)view.FindName("SwitchWorkspacesButton"));
            Ui.Settle();

            Assert.True(view.IsMenuOpen);
            Assert.True(menuOpen);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    /// <summary>Every control on the View tab is real now (Views, Show, Zoom and Window each got their own pass) - nothing left placed-but-disabled the way the Home tab still has a few Word controls with nothing behind them.</summary>
    [Fact]
    public void EveryControlOnTheViewTab_IsEnabled() => Ui.Run(() =>
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
                "ThemeButton",
                "ReadModeButton", "DraftButton", "OutlineButton", "RulerCheckBox", "GridlinesCheckBox", "NavigationPaneCheckBox",
                "ZoomInButton", "ZoomOutButton", "ZoomResetButton",
                "OnePageButton", "SwitchWorkspacesButton", "SwitchNotesButton",
            ];
            foreach (var name in working) Assert.True(((Control)view.FindName(name)).IsEnabled, name);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void RibbonTiles_ShowNoFrame_UntilThePointerIsOnThem() => Ui.Run(() =>
    {
        // Word's own ribbon buttons are just a glyph and a caption at rest - no box, no border - until hovered.
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);

            foreach (var name in new[] { "ZoomInButton", "ZoomResetButton", "ThemeButton", "SwitchNotesButton", "SwitchWorkspacesButton" })
            {
                var button = (Button)view.FindName(name);
                var frame = Ui.Descendants<Border>(button).First(b => b.Name == "Frame");
                Assert.True(((SolidColorBrush)frame.Background).Color == Colors.Transparent, $"{name} has a background at rest");
                Assert.True(((SolidColorBrush)frame.BorderBrush).Color == Colors.Transparent, $"{name} has a border at rest");
            }

            // OnePageButton is a ToggleButton (RibbonToggleTile), not a Button, but shares the same flat-until-hover Frame.
            var onePage = (ToggleButton)view.FindName("OnePageButton");
            var onePageFrame = Ui.Descendants<Border>(onePage).First(b => b.Name == "Frame");
            Assert.True(((SolidColorBrush)onePageFrame.Background).Color == Colors.Transparent, "OnePageButton has a background at rest");
            Assert.True(((SolidColorBrush)onePageFrame.BorderBrush).Color == Colors.Transparent, "OnePageButton has a border at rest");
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheReadModeButton_TogglesTheFocusedNotesReadMode() => Ui.Run(() =>
    {
        var (app, note) = FixtureWithNote();
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var toggle = (ToggleButton)view.FindName("ReadModeButton");
            Assert.False(toggle.IsChecked);

            toggle.IsChecked = true;
            Ui.Settle();

            Assert.True(note.IsReadOnly);
            Assert.True(note.IsFullscreen);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheDraftAndOutlineButtons_AreMutuallyExclusive() => Ui.Run(() =>
    {
        var (app, note) = FixtureWithNote();
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var view = new ViewRibbonView { DataContext = settings };
            using var window = new WindowHolder(view);
            var draft = (ToggleButton)view.FindName("DraftButton");
            var outline = (ToggleButton)view.FindName("OutlineButton");

            draft.IsChecked = true;
            Ui.Settle();
            Assert.True(note.HideImages);

            outline.IsChecked = true;
            Ui.Settle();

            Assert.True(note.IsOutlineView);
            Assert.False(note.HideImages); // entering Outline leaves Draft, they're views of the same note, not independent flags
            Assert.False(draft.IsChecked);
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
