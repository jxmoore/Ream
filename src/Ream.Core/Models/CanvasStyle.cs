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
    /// How strong the backing plate behind Ream's own text (tab names, labels, the title) is: none while the canvas is
    /// solid, and up to 85% when it is fully clear, so light text never floats straight over a bright desktop.
    /// </summary>
    public static byte PlateAlpha(int canvasOpacityPercent) =>
        (byte)Math.Round(255 * 0.85 * (100 - Math.Clamp(canvasOpacityPercent, 0, 100)) / 100.0, MidpointRounding.AwayFromZero);

    /// <summary>The alpha (0-255) of a note's background. Unlike the canvas it may reach 0: the canvas under it keeps the window clickable.</summary>
    public static byte NoteAlpha(int opacityPercent) =>
        (byte)Math.Round(Math.Clamp(opacityPercent, 0, 100) * 255 / 100.0, MidpointRounding.AwayFromZero);
}
