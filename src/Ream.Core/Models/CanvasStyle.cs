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

    /// <summary>
    /// Whether blurring the desktop behind the window makes sense at this opacity. At 0% the point is that nothing
    /// of Ream sits behind the notes, and a blurred desktop is something of Ream: it is only ever asked for above 0.
    /// </summary>
    public static bool AllowsBlur(int opacityPercent) => Math.Clamp(opacityPercent, 0, 100) > 0;

    /// <summary>
    /// The alpha of the backdrop that appears behind Ream's own text when the pointer is over it: the canvas alpha while
    /// that is solid enough, and up to 85% once the canvas is nearly clear, so light text is readable over a bright desktop
    /// at the moment you reach for it (and stays out of the way, fully clear, the rest of the time).
    /// </summary>
    public static byte HoverBackdropAlpha(int canvasOpacityPercent)
    {
        int clamped = Math.Clamp(canvasOpacityPercent, 0, 100);
        byte backdrop = (byte)Math.Round(255 * 0.85 * (100 - clamped) / 100.0, MidpointRounding.AwayFromZero);
        return Math.Max(Alpha(clamped), backdrop);
    }

    /// <summary>The alpha (0-255) of a note's background. Unlike the canvas it may reach 0: the canvas under it keeps the window clickable.</summary>
    public static byte NoteAlpha(int opacityPercent) =>
        (byte)Math.Round(Math.Clamp(opacityPercent, 0, 100) * 255 / 100.0, MidpointRounding.AwayFromZero);
}
