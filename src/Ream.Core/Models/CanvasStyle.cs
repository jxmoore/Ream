namespace Ream.Core.Models;

/// <summary>How see-through the canvas behind the notes is. The window is drawn by Ream itself, so this works everywhere.</summary>
public static class CanvasStyle
{
    /// <summary>Anything below 100% lets the desktop show through.</summary>
    public static bool IsSeeThrough(int opacityPercent) => Math.Clamp(opacityPercent, 0, 100) < 100;

    /// <summary>
    /// The alpha (1-255) of the canvas color. Never 0: a fully transparent pixel of a see-through window
    /// lets the mouse fall through to whatever is behind it, so "0%" is one step from invisible instead.
    /// </summary>
    public static byte Alpha(int opacityPercent) =>
        (byte)Math.Max(1, Math.Round(Math.Clamp(opacityPercent, 0, 100) * 255 / 100.0, MidpointRounding.AwayFromZero));
}
