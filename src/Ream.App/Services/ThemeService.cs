using System.Windows;
using System.Windows.Media;
using Ream.Core.Models;

namespace Ream.App.Services;

/// <summary>
/// Puts the chosen color theme into the app's resources: swaps the palette, and publishes the two brushes that
/// depend on settings as well as the palette - the canvas behind the notes (its see-through amount) and the
/// border around the focused note.
/// </summary>
internal sealed class ThemeService
{
    public const string CanvasBrushKey = "CanvasBrush";
    public const string FocusBorderBrushKey = "FocusBorderBrush";

    private readonly Application _application;
    private readonly Func<bool> _backdropSupported;

    private string? _paletteId;
    private Color _canvas;
    private Color _focusBorder;
    private WindowAppearance _appearance;

    /// <param name="backdropSupported">
    /// Whether Windows can blur behind the window (Windows 11). Without it the canvas stays solid; injectable so
    /// tests don't depend on the machine they run on.
    /// </param>
    public ThemeService(Application application, Func<bool>? backdropSupported = null)
    {
        _application = application;
        _backdropSupported = backdropSupported ?? (() => SystemBackdropSupport.IsAvailable);
    }

    /// <summary>Raised whenever the palette, the canvas or the focus border color actually changes.</summary>
    public event Action? Changed;

    public bool IsLight => _appearance.IsLight;

    public string ThemeId => _paletteId ?? ThemeCatalog.DefaultId;

    /// <summary>What the window's own chrome (title bar, blur) should look like to match.</summary>
    public WindowAppearance Appearance => _appearance;

    public void Apply(AppConfig config) =>
        Apply(config.Theme, config.CanvasOpacity, config.CanvasBlur, config.Layout.FocusBorderColor);

    public void Apply(string? themeId, int canvasOpacity = 100, bool canvasBlur = true, string? focusBorderColor = null)
    {
        var theme = ThemeCatalog.Resolve(themeId);
        bool changed = false;

        if (theme.Id != _paletteId)
        {
            var palette = new ResourceDictionary
            {
                Source = new Uri($"/Ream.App;component/Themes/{theme.FileName}", UriKind.Relative),
            };
            var merged = _application.Resources.MergedDictionaries;
            if (merged.Count > 0) merged[0] = palette;
            else merged.Insert(0, palette);

            _paletteId = theme.Id;
            changed = true;
        }

        bool supported = _backdropSupported();
        var solidCanvas = ((SolidColorBrush)_application.Resources["WindowBackgroundBrush"]).Color;
        var canvas = Color.FromArgb(CanvasStyle.Alpha(canvasOpacity, canvasBlur, supported), solidCanvas.R, solidCanvas.G, solidCanvas.B);

        var accent = ((SolidColorBrush)_application.Resources["AccentBrush"]).Color;
        var focusBorder = Rgba.TryParse(focusBorderColor, out var custom)
            ? Color.FromArgb(custom.A, custom.R, custom.G, custom.B)
            : accent;

        if (canvas != _canvas || changed) { Publish(CanvasBrushKey, canvas); changed = true; }
        if (focusBorder != _focusBorder || changed) { Publish(FocusBorderBrushKey, focusBorder); changed = true; }

        _canvas = canvas;
        _focusBorder = focusBorder;

        var appearance = new WindowAppearance(
            solidCanvas,
            ((SolidColorBrush)_application.Resources["TextBrush"]).Color,
            theme.IsLight,
            CanvasStyle.IsSeeThrough(canvasOpacity, canvasBlur, supported));

        changed |= appearance != _appearance;
        _appearance = appearance;

        if (changed) Changed?.Invoke();
    }

    private void Publish(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _application.Resources[key] = brush;
    }
}

/// <summary>What the window's chrome needs to know to match the theme.</summary>
internal readonly record struct WindowAppearance(Color Canvas, Color Text, bool IsLight, bool SeeThrough);

/// <summary>The system backdrop (blur behind the window) arrived in Windows 11 22H2, build 22621.</summary>
internal static class SystemBackdropSupport
{
    public const int MinimumBuild = 22621;

    public static bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumBuild);
}
