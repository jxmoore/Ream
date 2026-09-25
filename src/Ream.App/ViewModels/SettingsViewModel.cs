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
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.Config)) Sync();
        };
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
        _pendingTheme = null;
        _pendingOpacity = null;
        _pendingNoteOpacity = null;
        _pendingZoom = null;
        _pendingGapPx = null;
        _pendingCenterFocusedColumn = null;
        if (_store is null || (theme is null && opacity is null && noteOpacity is null && zoom is null && gapPx is null && centerFocusedColumn is null))
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
