using System.Globalization;

namespace Ream.Core.Models;

/// <summary>The named widths. Stored widths are plain fractions; this enum only reads older layout files.</summary>
public enum WidthPreset
{
    OneThird,
    Half,
    TwoThirds,
    Full,
}

/// <summary>Column widths are a fraction of the row's viewport: any value in range, with named presets to cycle through.</summary>
public static class WidthPresets
{
    public const double Min = 0.15;
    public const double Max = 1.0;
    public const double Default = 0.5;

    private const double MatchTolerance = 0.005;
    private static readonly double[] Values = [1d / 3d, 0.5, 2d / 3d, 1d];
    private static readonly string[] Labels = ["1/3", "1/2", "2/3", "Full"];

    public static double Fraction(this WidthPreset preset)
    {
        int index = (int)preset;
        return (uint)index < Values.Length ? Values[index] : Default;
    }

    /// <summary>Keeps a fraction usable; anything not a real number falls back to the default.</summary>
    public static double Clamp(double fraction) =>
        double.IsFinite(fraction) ? Math.Clamp(fraction, Min, Max) : Default;

    /// <summary>The step Alt+= / Alt+- take, as a share of the row.</summary>
    public const double Step = 0.05;

    /// <summary>The width one step wider (or narrower for a negative <paramref name="direction"/>), kept in range.</summary>
    public static double Nudge(double current, int direction) =>
        Clamp(Math.Round(current + Math.Sign(direction) * Step, 4));

    /// <summary>The next preset wider than the current width, wrapping to the narrowest after the widest.</summary>
    public static double Next(double current)
    {
        foreach (double value in Values)
            if (value > current + MatchTolerance) return value;
        return Values[0];
    }

    /// <summary>"1/2" for a preset width, otherwise a percentage.</summary>
    public static string Label(double fraction)
    {
        for (int i = 0; i < Values.Length; i++)
            if (Math.Abs(Values[i] - fraction) < MatchTolerance) return Labels[i];

        return Math.Round(fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
