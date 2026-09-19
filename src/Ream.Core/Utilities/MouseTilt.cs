namespace Ream.Core.Utilities;

/// <summary>Reads the horizontal ("tilt") wheel message, which WPF doesn't surface as an event.</summary>
public static class MouseTilt
{
    public const int WmMouseHWheel = 0x020E;

    /// <param name="wParam">The message's wParam; the wheel delta is its high word (positive = tilted right).</param>
    public static bool TryGetDelta(int message, long wParam, out int delta)
    {
        delta = 0;
        if (message != WmMouseHWheel) return false;

        delta = (short)((wParam >> 16) & 0xFFFF);
        return true;
    }
}
