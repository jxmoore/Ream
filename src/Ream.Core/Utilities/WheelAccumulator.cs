namespace Ream.Core.Utilities;

/// <summary>
/// Turns raw mouse-wheel deltas into whole discrete steps. A standard wheel
/// notch is 120; precision touchpads send smaller deltas that accumulate.
/// </summary>
public sealed class WheelAccumulator
{
    private const int NotchDelta = 120;
    private int _accumulated;

    /// <returns>Whole steps (positive = wheel up / away from user); 0 if not yet a full notch.</returns>
    public int Add(int delta)
    {
        if (_accumulated != 0 && Math.Sign(delta) != Math.Sign(_accumulated))
            _accumulated = 0;

        _accumulated += delta;
        int steps = _accumulated / NotchDelta;
        _accumulated -= steps * NotchDelta;
        return steps;
    }

    public void Reset() => _accumulated = 0;
}
