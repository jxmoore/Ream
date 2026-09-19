using Ream.Core.Layout;
using Ream.Core.Models;
using Ream.Core.Utilities;

namespace Ream.Tests;

public class WidthPresetsTests
{
    [Theory]
    [InlineData(1d / 3d, 0.5)]
    [InlineData(0.5, 2d / 3d)]
    [InlineData(2d / 3d, 1.0)]
    [InlineData(1.0, 1d / 3d)]
    [InlineData(0.2, 1d / 3d)]
    [InlineData(0.4, 0.5)]
    [InlineData(0.55, 2d / 3d)]
    [InlineData(0.9, 1.0)]
    [InlineData(0.503, 2d / 3d)]
    public void Next_MovesToTheNextWiderPreset_AndWrapsAfterFull(double current, double expected)
    {
        Assert.Equal(expected, WidthPresets.Next(current), 9);
    }

    [Theory]
    [InlineData(1d / 3d, "1/3")]
    [InlineData(0.5, "1/2")]
    [InlineData(0.6667, "2/3")]
    [InlineData(1.0, "Full")]
    [InlineData(0.37, "37%")]
    [InlineData(0.9, "90%")]
    public void Label_NamesPresets_AndShowsOtherWidthsAsPercentages(double fraction, string expected)
    {
        Assert.Equal(expected, WidthPresets.Label(fraction));
    }

    [Theory]
    [InlineData(0.4, 0.4)]
    [InlineData(0.01, 0.15)]
    [InlineData(-3, 0.15)]
    [InlineData(7, 1.0)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(double.PositiveInfinity, 0.5)]
    public void Clamp_KeepsWidthsUsable(double input, double expected)
    {
        Assert.Equal(expected, WidthPresets.Clamp(input), 9);
    }

    [Theory]
    [InlineData(WidthPreset.OneThird, 1d / 3d)]
    [InlineData(WidthPreset.Half, 0.5)]
    [InlineData(WidthPreset.TwoThirds, 2d / 3d)]
    [InlineData(WidthPreset.Full, 1d)]
    [InlineData((WidthPreset)99, 0.5)]
    public void LegacyPresets_MapToFractions(WidthPreset preset, double expected)
    {
        Assert.Equal(expected, preset.Fraction(), 9);
    }

    [Fact]
    public void CyclingFromEveryPreset_VisitsThemAllInOrder()
    {
        double width = 1d / 3d;
        var labels = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            labels.Add(WidthPresets.Label(width));
            width = WidthPresets.Next(width);
        }

        Assert.Equal(["1/3", "1/2", "2/3", "Full", "1/3"], labels);
    }
}

public class FractionForWidthTests
{
    [Theory]
    [InlineData(300)]
    [InlineData(470)]
    [InlineData(960)]
    public void IsTheInverseOfColumnWidth(double width)
    {
        double fraction = RowLayout.FractionForWidth(width, 1000, 20);

        Assert.Equal(width, RowLayout.ColumnWidth(fraction, 1000, 20), 6);
    }

    [Fact]
    public void ExactPresetWidths_GiveThePresetFractions()
    {
        double half = RowLayout.ColumnWidth(0.5, 1000, 20);

        Assert.Equal(0.5, RowLayout.FractionForWidth(half, 1000, 20), 9);
    }

    [Fact]
    public void AbsurdWidths_AreKeptInRange()
    {
        Assert.Equal(WidthPresets.Min, RowLayout.FractionForWidth(1, 1000, 20));
        Assert.Equal(WidthPresets.Max, RowLayout.FractionForWidth(50_000, 1000, 20));
    }

    [Fact]
    public void ANarrowerThanGapViewport_FallsBackToTheDefault()
    {
        Assert.Equal(WidthPresets.Default, RowLayout.FractionForWidth(100, 10, 20));
        Assert.Equal(WidthPresets.Default, RowLayout.FractionForWidth(100, 0, 0));
    }
}

public class MouseTiltTests
{
    private static long WParam(int delta, int keyState = 0) => ((long)(ushort)(short)delta << 16) | (uint)keyState;

    [Theory]
    [InlineData(120)]
    [InlineData(-120)]
    [InlineData(60)]
    [InlineData(-360)]
    public void ReadsTheSignedDeltaFromTheHighWord(int delta)
    {
        Assert.True(MouseTilt.TryGetDelta(MouseTilt.WmMouseHWheel, WParam(delta), out int read));
        Assert.Equal(delta, read);
    }

    [Fact]
    public void IgnoresTheModifierKeysInTheLowWord()
    {
        Assert.True(MouseTilt.TryGetDelta(MouseTilt.WmMouseHWheel, WParam(120, keyState: 0x0008), out int read));
        Assert.Equal(120, read);
    }

    [Fact]
    public void IgnoresOtherMessages()
    {
        Assert.False(MouseTilt.TryGetDelta(0x020A, WParam(120), out int read));
        Assert.Equal(0, read);
    }
}
