namespace Ream.Core.Models;

public enum WidthPreset
{
    OneThird,
    Half,
    TwoThirds,
    Full,
}

public static class WidthPresetExtensions
{
    public static double Fraction(this WidthPreset preset) => preset switch
    {
        WidthPreset.OneThird => 1d / 3d,
        WidthPreset.Half => 0.5,
        WidthPreset.TwoThirds => 2d / 3d,
        WidthPreset.Full => 1d,
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    public static WidthPreset Next(this WidthPreset preset) => preset switch
    {
        WidthPreset.OneThird => WidthPreset.Half,
        WidthPreset.Half => WidthPreset.TwoThirds,
        WidthPreset.TwoThirds => WidthPreset.Full,
        _ => WidthPreset.OneThird,
    };

    public static string Label(this WidthPreset preset) => preset switch
    {
        WidthPreset.OneThird => "1/3",
        WidthPreset.Half => "1/2",
        WidthPreset.TwoThirds => "2/3",
        WidthPreset.Full => "Full",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };
}
