namespace Ream.Core.Models;

/// <summary>
/// How see-through the canvas behind the notes is. Transparency only works through the system backdrop
/// (blur), so without it - the setting is off, or Windows is too old - the canvas simply stays solid.
/// </summary>
public static class CanvasStyle
{
    public static bool IsSeeThrough(int opacityPercent, bool blur, bool backdropSupported) =>
        blur && backdropSupported && Math.Clamp(opacityPercent, 0, 100) < 100;

    /// <summary>The alpha (0-255) of the canvas color.</summary>
    public static byte Alpha(int opacityPercent, bool blur, bool backdropSupported) =>
        blur && backdropSupported
            ? (byte)Math.Round(Math.Clamp(opacityPercent, 0, 100) * 255 / 100.0)
            : (byte)255;
}
