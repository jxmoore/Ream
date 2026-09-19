using System.Windows;
using System.Windows.Media;
using Ream.Core.Models;

namespace Ream.App.Services;

/// <summary>
/// Puts the chosen color theme into the app's resources: swaps the palette, and publishes the brushes that depend
/// on settings as well as the palette - the canvas behind the notes (its see-through amount), the ribbon panel
/// (which follows the canvas but never gets too faint to read), and the border around the focused note.
/// </summary>
internal sealed class ThemeService
{
    public const string CanvasBrushKey = "CanvasBrush";
    public const string RibbonBrushKey = "RibbonBrush";
    public const string FocusBorderBrushKey = "FocusBorderBrush";

    /// <summary>The ribbon floats over notes when it pops out, so its own 11 px labels need something behind them.</summary>
    public const byte RibbonMinimumAlpha = 153; // 60%

    private readonly Application _application;
    private readonly Func<bool> _blurSupported;

    private string? _paletteId;
    private Color _canvas;
    private Color _ribbon;
    private Color _focusBorder;
    private bool _isLight;
    private WindowAppearance _appearance;

    /// <param name="blurSupported">
    /// Whether Windows can blur behind the window (Windows 10 1803 or later). Only decides whether blur is asked for;
    /// see-through works regardless. Injectable so tests don't depend on the machine they run on.
    /// </param>
    public ThemeService(Application application, Func<bool>? blurSupported = null)
    {
        _application = application;
        _blurSupported = blurSupported ?? (() => BlurSupport.IsAvailable);
    }

    /// <summary>Raised whenever the palette, the canvas or the focus border color actually changes.</summary>
    public event Action? Changed;

    public bool IsLight => _isLight;

    public string ThemeId => _paletteId ?? ThemeCatalog.DefaultId;

    /// <summary>What the window itself needs to know beyond the brushes: whether to blur behind it.</summary>
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
            _isLight = theme.IsLight;
            changed = true;
        }

        byte alpha = CanvasStyle.Alpha(canvasOpacity);
        var canvas = WithAlpha(BrushColor("WindowBackgroundBrush"), alpha);
        var ribbon = WithAlpha(BrushColor("ToolbarBrush"), Math.Max(alpha, RibbonMinimumAlpha));

        var accent = BrushColor("AccentBrush");
        var focusBorder = Rgba.TryParse(focusBorderColor, out var custom)
            ? Color.FromArgb(custom.A, custom.R, custom.G, custom.B)
            : accent;

        if (canvas != _canvas || changed) { Publish(CanvasBrushKey, canvas); changed = true; }
        if (ribbon != _ribbon || changed) { Publish(RibbonBrushKey, ribbon); changed = true; }
        if (focusBorder != _focusBorder || changed) { Publish(FocusBorderBrushKey, focusBorder); changed = true; }

        _canvas = canvas;
        _ribbon = ribbon;
        _focusBorder = focusBorder;

        bool seeThrough = CanvasStyle.IsSeeThrough(canvasOpacity);
        var appearance = new WindowAppearance(seeThrough, Blur: seeThrough && canvasBlur && _blurSupported());
        changed |= appearance != _appearance;
        _appearance = appearance;

        if (changed) Changed?.Invoke();
    }

    private Color BrushColor(string key) => ((SolidColorBrush)_application.Resources[key]).Color;

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private void Publish(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _application.Resources[key] = brush;
    }
}

/// <summary>What the window's chrome needs to know: whether the canvas is see-through, and whether to blur behind it.</summary>
internal readonly record struct WindowAppearance(bool SeeThrough, bool Blur);

/// <summary>Blur-behind arrived on Windows 10 1803 (build 17134).</summary>
internal static class BlurSupport
{
    public const int MinimumBuild = 17134;

    public static bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumBuild);
}
