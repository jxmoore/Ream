using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ream.App.Services;
using Ream.Core.Models;
using Ream.Core.Utilities;
using Ream.Persistence.Storage;

namespace Ream.App.ViewModels;

/// <summary>One theme in the settings list, with the colors its swatch shows.</summary>
public sealed partial class ThemeOption : ObservableObject
{
    internal ThemeOption(ThemeInfo info, Brush canvas, Brush card, Brush accent, Brush text)
    {
        Id = info.Id;
        Name = info.Name;
        Canvas = canvas;
        Card = card;
        Accent = accent;
        Text = text;
    }

    public string Id { get; }
    public string Name { get; }
    public Brush Canvas { get; }
    public Brush Card { get; }
    public Brush Accent { get; }
    public Brush Text { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// What the View tab edits: which theme is in use and how opaque the canvas and the notes are. Changes apply at
/// once and are written to config.json shortly after (so dragging a slider doesn't write on every step).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly AppViewModel _app;
    private readonly ThemeService _theme;
    private readonly AppConfigStore? _store;
    private readonly DebouncedAction _save;
    private readonly Func<bool> _blurSupported;

    private bool _syncing;
    private string? _pendingTheme;
    private int? _pendingOpacity;
    private int? _pendingNoteOpacity;
    private int? _pendingZoom;
    private double? _pendingGapPx;
    private bool? _pendingCenterFocusedColumn;
    private bool? _pendingRibbonPinned;

    /// <param name="store">Where changes are saved; null keeps them for this run only.</param>
    /// <param name="dispatcher">Runs the delayed save on the UI thread; null runs it on a pool thread.</param>
    internal SettingsViewModel(
        AppViewModel app,
        ThemeService theme,
        AppConfigStore? store = null,
        Dispatcher? dispatcher = null,
        Func<bool>? blurSupported = null)
    {
        _app = app;
        _theme = theme;
        _store = store;
        _blurSupported = blurSupported ?? (() => BlurSupport.IsAvailable);
        _save = new DebouncedAction(Persist, SaveDelay, dispatcher is null ? null : action => dispatcher.BeginInvoke(action));

        Themes = ThemeCatalog.All.Select(ThemeSwatches.Load).ToList();
        Sync();
        app.PropertyChanged += OnAppPropertyChanged;
        TrackFocus();

        // GridlinesOn defaults to false, but a plain [ObservableProperty] default never runs its own OnChanged -
        // without this, the resource NoteColumnView reads would simply never exist yet, and a missing DynamicResource
        // leaves Visibility at its own default (Visible), showing gridlines on every note before anyone asked for them.
        OnGridlinesOnChanged(GridlinesOn);
    }

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.Config)) Sync();
        if (e.PropertyName == nameof(AppViewModel.CurrentIndex)) TrackFocus();
        if (e.PropertyName == nameof(AppViewModel.OnePageMode)) OnPropertyChanged(nameof(OnePageMode));
    }

    public IReadOnlyList<ThemeOption> Themes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityLabel))]
    private int _opacityPercent = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteOpacityLabel))]
    private int _noteOpacityPercent = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    [NotifyPropertyChangedFor(nameof(ZoomResetTooltip))]
    private int _zoomPercent = 100;

    [ObservableProperty]
    private double _gapPx = 28;

    [ObservableProperty]
    private bool _centerFocusedColumn = true;

    /// <summary>
    /// Show group's Gridlines: a faint grid on each note (not the canvas behind them). Session-only, not saved to
    /// config.json. Published as an Application resource, already a Visibility so NoteColumnView's own grid overlay
    /// can bind straight to it with no converter - the same trick NoteZoomScale uses, since NoteColumnView's
    /// DataContext is the note, not the settings.
    /// </summary>
    [ObservableProperty]
    private bool _gridlinesOn;

    internal const string GridlinesVisibilityKey = "GridlinesVisibility";

    partial void OnGridlinesOnChanged(bool value) =>
        Application.Current.Resources[GridlinesVisibilityKey] = value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Show group's Navigation Pane: docked to the side of the canvas. Session-only, not saved to config.json.</summary>
    [ObservableProperty]
    private bool _navigationPaneOpen;

    /// <summary>
    /// Zoom group's Board Zoom dropdown: how far the workspace strip is pulled back, 0-100 (100 = normal - the
    /// board isn't pulled back at all), so neighboring workspaces come into view the lower it goes. Session-only,
    /// not saved to config.json - MainWindow reacts to it directly (OnSettingsPropertyChanged), the same as
    /// Gridlines/NavigationPaneOpen, since it drives the window's own canvas transform, not anything this view
    /// model owns. Ctrl+Alt+Scroll (config's own "boardZoomWheel", read by MainWindow directly - there's no single
    /// command a wheel gesture could bind to the way a keybinding normally does) changes this same property.
    /// </summary>
    [ObservableProperty]
    private int _boardZoomPercent = 100;

    partial void OnBoardZoomPercentChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 100);
        if (clamped != value) BoardZoomPercent = clamped;
    }

    /// <summary>
    /// Home tab's own Pan toggle: a click-and-drag-to-pan mode that stays on until clicked again, rather than the
    /// same panning held on with Alt+X (config's own "panCanvas", read by MainWindow directly for the same
    /// reason boardZoomWheel is - a hold isn't a command either). Session-only, not saved to config.json - reacted
    /// to directly by MainWindow (OnSettingsPropertyChanged), which owns the actual drag/cursor handling; this is
    /// just the on/off switch, the same shape Gridlines/NavigationPaneOpen already are.
    /// </summary>
    [ObservableProperty]
    private bool _panModeOn;

    /// <summary>The Window group's Switch Notes / Switch Workspaces menus need the app view model's own workspaces and notes.</summary>
    internal AppViewModel App => _app;

    // ----- Passthroughs onto the focused note / the app, for View-tab controls whose state lives there rather than
    // here (this view model's own DataContext is what the ribbon binds to, so a CheckBox/ToggleButton showing real
    // on/off state needs a property on THIS object even though the truth is a note's or the app's). Each setter
    // just asks the matching AppViewModel command to toggle; the resulting PropertyChanged on the note (or the app,
    // for OnePageMode) is what actually updates the value, so the property converges to the truth either way. -----

    private NoteViewModel? _trackedNote;
    private WorkspaceViewModel? _trackedWorkspace;

    private void TrackFocus()
    {
        var workspace = _app.CurrentWorkspace;
        if (!ReferenceEquals(workspace, _trackedWorkspace))
        {
            if (_trackedWorkspace is not null) _trackedWorkspace.PropertyChanged -= OnTrackedWorkspaceChanged;
            _trackedWorkspace = workspace;
            workspace.PropertyChanged += OnTrackedWorkspaceChanged;
        }

        var note = workspace.FocusedNote;
        if (ReferenceEquals(note, _trackedNote)) return;

        if (_trackedNote is not null) _trackedNote.PropertyChanged -= OnTrackedNoteChanged;
        _trackedNote = note;
        if (_trackedNote is not null) _trackedNote.PropertyChanged += OnTrackedNoteChanged;
        RaiseFocusedNoteProperties();
    }

    private void OnTrackedWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.FocusedIndex)) TrackFocus();
    }

    private void OnTrackedNoteChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NoteViewModel.IsReadOnly): OnPropertyChanged(nameof(FocusedNoteIsReadOnly)); break;
            case nameof(NoteViewModel.HideImages): OnPropertyChanged(nameof(FocusedNoteHideImages)); break;
            case nameof(NoteViewModel.IsOutlineView): OnPropertyChanged(nameof(FocusedNoteIsOutlineView)); break;
            case nameof(NoteViewModel.ShowRuler): OnPropertyChanged(nameof(FocusedNoteShowRuler)); break;
        }
    }

    private void RaiseFocusedNoteProperties()
    {
        OnPropertyChanged(nameof(FocusedNoteIsReadOnly));
        OnPropertyChanged(nameof(FocusedNoteHideImages));
        OnPropertyChanged(nameof(FocusedNoteIsOutlineView));
        OnPropertyChanged(nameof(FocusedNoteShowRuler));
    }

    /// <summary>Views' Read Mode CheckBox.</summary>
    public bool FocusedNoteIsReadOnly
    {
        get => _app.CurrentWorkspace.FocusedNote?.IsReadOnly ?? false;
        set { if (value != FocusedNoteIsReadOnly) _app.ToggleReadModeCommand.Execute(null); }
    }

    /// <summary>Views' Draft CheckBox.</summary>
    public bool FocusedNoteHideImages
    {
        get => _app.CurrentWorkspace.FocusedNote?.HideImages ?? false;
        set { if (value != FocusedNoteHideImages) _app.ToggleDraftViewCommand.Execute(null); }
    }

    /// <summary>Views' Outline CheckBox.</summary>
    public bool FocusedNoteIsOutlineView
    {
        get => _app.CurrentWorkspace.FocusedNote?.IsOutlineView ?? false;
        set { if (value != FocusedNoteIsOutlineView) _app.ToggleOutlineViewCommand.Execute(null); }
    }

    /// <summary>Show's Ruler CheckBox.</summary>
    public bool FocusedNoteShowRuler
    {
        get => _app.CurrentWorkspace.FocusedNote?.ShowRuler ?? false;
        set { if (value != FocusedNoteShowRuler) _app.ToggleRulerCommand.Execute(null); }
    }

    /// <summary>Window's One Page ToggleButton.</summary>
    public bool OnePageMode
    {
        get => _app.OnePageMode;
        set { if (value != _app.OnePageMode) _app.ToggleOnePageCommand.Execute(null); }
    }

    [ObservableProperty]
    private string _selectedThemeId = ThemeCatalog.DefaultId;

    /// <summary>The dropdown's selection: reads the theme in use, and choosing one applies it.</summary>
    public ThemeOption? SelectedTheme
    {
        get => Themes.FirstOrDefault(t => t.IsSelected);
        set
        {
            if (value is not null) SelectThemeCommand.Execute(value);
        }
    }

    public string OpacityLabel => $"{OpacityPercent}%";

    public string NoteOpacityLabel => $"{NoteOpacityPercent}%";

    public string ZoomLabel => $"{ZoomPercent}%";

    public string ZoomResetTooltip => ZoomPercent == 100 ? "Zoom is 100%" : $"Zoom is {ZoomPercent}% - click to reset to 100%";

    internal const int MinZoomPercent = 50;
    internal const int MaxZoomPercent = 200;
    internal const double MinGapPx = 8;
    internal const double MaxGapPx = 60;

    /// <summary>What the slider does, and whether the blur behind the see-through canvas is on (canvasBlur in config.json).</summary>
    public string OpacityHint => !_app.Config.CanvasBlur
        ? "Lower it to see the desktop through Ream. Blur is off (canvasBlur in config.json)."
        : !_blurSupported()
            ? "Lower it to see the desktop through Ream. Blur needs Windows 10 (1803) or later."
            : "Lower it to see the desktop through Ream, blurred.";

    [RelayCommand]
    private void SelectTheme(ThemeOption? option)
    {
        if (option is null || option.Id == SelectedThemeId) return;
        Change(theme: option.Id, opacity: null, noteOpacity: null, zoom: null);
    }

    partial void OnOpacityPercentChanged(int value)
    {
        if (_syncing) return;

        int clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            OpacityPercent = clamped;
            return;
        }
        Change(theme: null, opacity: clamped, noteOpacity: null, zoom: null);
    }

    partial void OnNoteOpacityPercentChanged(int value)
    {
        if (_syncing) return;

        int clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            NoteOpacityPercent = clamped;
            return;
        }
        Change(theme: null, opacity: null, noteOpacity: clamped, zoom: null);
    }

    partial void OnZoomPercentChanged(int value)
    {
        if (_syncing) return;

        int clamped = Math.Clamp(value, MinZoomPercent, MaxZoomPercent);
        if (clamped != value)
        {
            ZoomPercent = clamped;
            return;
        }
        Change(theme: null, opacity: null, noteOpacity: null, zoom: clamped);
    }

    partial void OnGapPxChanged(double value)
    {
        if (_syncing) return;

        double clamped = Math.Clamp(value, MinGapPx, MaxGapPx);
        if (Math.Abs(clamped - value) > 0.01)
        {
            GapPx = clamped;
            return;
        }
        ChangeLayout(gapPx: clamped, centerFocusedColumn: null);
    }

    partial void OnCenterFocusedColumnChanged(bool value)
    {
        if (_syncing) return;
        ChangeLayout(gapPx: null, centerFocusedColumn: value);
    }

    private void Change(string? theme, int? opacity, int? noteOpacity, int? zoom)
    {
        var next = _app.Config.With(theme, opacity, noteOpacity, zoom: zoom);

        _syncing = true;
        try
        {
            _app.Config = next;
            _theme.Apply(next);
            ShowCurrent(next);
        }
        finally
        {
            _syncing = false;
        }

        if (theme is not null) _pendingTheme = theme;
        if (opacity is not null) _pendingOpacity = opacity;
        if (noteOpacity is not null) _pendingNoteOpacity = noteOpacity;
        if (zoom is not null) _pendingZoom = zoom;
        _save.Trigger();
    }

    /// <summary>Gap size and centered focus (the "Layout" group): a nested config.json section, saved separately from the flat settings above.</summary>
    private void ChangeLayout(double? gapPx, bool? centerFocusedColumn)
    {
        var next = _app.Config.WithLayout(gapPx, centerFocusedColumn);

        _syncing = true;
        try
        {
            _app.Config = next;
            ShowCurrent(next);
        }
        finally
        {
            _syncing = false;
        }

        if (gapPx is not null) _pendingGapPx = gapPx;
        if (centerFocusedColumn is not null) _pendingCenterFocusedColumn = centerFocusedColumn;
        _save.Trigger();
    }

    /// <summary>Called by MainWindow's own pin button (not bound from XAML - the ribbon's pin state lives in
    /// MainWindow's RibbonVisibility, not here) so the choice survives a restart, the same "update in memory, save
    /// shortly after" shape every other setting here already uses.</summary>
    internal void SetRibbonPinned(bool pinned)
    {
        if (_syncing) return;

        var next = _app.Config.WithRibbon(pinned: pinned);

        _syncing = true;
        try
        {
            _app.Config = next;
        }
        finally
        {
            _syncing = false;
        }

        _pendingRibbonPinned = pinned;
        _save.Trigger();
    }

    /// <summary>Follows the config when it changes elsewhere (the file was edited by hand and reloaded).</summary>
    private void Sync()
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            ShowCurrent(_app.Config);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ShowCurrent(AppConfig config)
    {
        string id = ThemeCatalog.Resolve(config.Theme).Id;
        SelectedThemeId = id;
        OpacityPercent = config.CanvasOpacity;
        NoteOpacityPercent = config.NoteOpacity;
        ZoomPercent = config.Zoom;
        GapPx = config.Layout.GapPx;
        CenterFocusedColumn = config.Layout.CenterFocusedColumn;
        foreach (var option in Themes) option.IsSelected = option.Id == id;
        OnPropertyChanged(nameof(SelectedTheme));

        OnPropertyChanged(nameof(OpacityHint));
    }

    private void Persist()
    {
        string? theme = _pendingTheme;
        int? opacity = _pendingOpacity;
        int? noteOpacity = _pendingNoteOpacity;
        int? zoom = _pendingZoom;
        double? gapPx = _pendingGapPx;
        bool? centerFocusedColumn = _pendingCenterFocusedColumn;
        bool? ribbonPinned = _pendingRibbonPinned;
        _pendingTheme = null;
        _pendingOpacity = null;
        _pendingNoteOpacity = null;
        _pendingZoom = null;
        _pendingGapPx = null;
        _pendingCenterFocusedColumn = null;
        _pendingRibbonPinned = null;
        if (_store is null || (theme is null && opacity is null && noteOpacity is null && zoom is null
            && gapPx is null && centerFocusedColumn is null && ribbonPinned is null))
            return;

        bool saved = _store.Update(root =>
        {
            if (theme is not null) root["theme"] = theme;
            if (opacity is not null) root["canvasOpacity"] = opacity.Value;
            if (noteOpacity is not null) root["noteOpacity"] = noteOpacity.Value;
            if (zoom is not null) root["zoom"] = zoom.Value;
            if (gapPx is not null || centerFocusedColumn is not null)
            {
                var layout = root["layout"] as JsonObject;
                if (layout is null)
                {
                    layout = new JsonObject();
                    root["layout"] = layout;
                }
                if (gapPx is not null) layout["gapPx"] = gapPx.Value;
                if (centerFocusedColumn is not null) layout["centerFocusedColumn"] = centerFocusedColumn.Value;
            }
            if (ribbonPinned is not null)
            {
                var ribbon = root["ribbon"] as JsonObject;
                if (ribbon is null)
                {
                    ribbon = new JsonObject();
                    root["ribbon"] = ribbon;
                }
                ribbon["pinned"] = ribbonPinned.Value;
            }
        }, out var error);

        if (!saved) _app.ConfigError = $"Settings not saved: {error}";
    }

    /// <summary>Writes any change still waiting for its delay. Call before the app exits.</summary>
    public void Flush() => _save.Flush();

    public void Dispose() => _save.Dispose();
}

/// <summary>Reads a theme's palette so the settings list can preview it without applying it.</summary>
internal static class ThemeSwatches
{
    public static ThemeOption Load(ThemeInfo info)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/Ream.App;component/Themes/{info.FileName}", UriKind.Relative),
        };

        static Brush Frozen(object brush)
        {
            var copy = ((SolidColorBrush)brush).Clone();
            copy.Freeze();
            return copy;
        }

        return new ThemeOption(
            info,
            Frozen(palette["WindowBackgroundBrush"]),
            Frozen(palette["CardBrush"]),
            Frozen(palette["AccentBrush"]),
            Frozen(palette["TextBrush"]));
    }
}
