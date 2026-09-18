using Ream.Core.Models;
using Ream.Core.Utilities;

namespace Ream.Tests;

public class WheelAccumulatorTests
{
    [Fact]
    public void FullNotch_IsOneStep()
    {
        var acc = new WheelAccumulator();
        Assert.Equal(1, acc.Add(120));
        Assert.Equal(-1, acc.Add(-120));
    }

    [Fact]
    public void SmallDeltas_AccumulateIntoAStep()
    {
        var acc = new WheelAccumulator();
        Assert.Equal(0, acc.Add(60));
        Assert.Equal(1, acc.Add(60));
    }

    [Fact]
    public void ReversingDirection_DropsTheLeftover()
    {
        var acc = new WheelAccumulator();
        Assert.Equal(0, acc.Add(60));
        Assert.Equal(-1, acc.Add(-120));
    }

    [Fact]
    public void FastSpin_ProducesMultipleSteps()
    {
        var acc = new WheelAccumulator();
        Assert.Equal(3, acc.Add(360));
    }
}

public class WidthPresetTests
{
    [Fact]
    public void Next_CyclesThroughAllPresets()
    {
        var p = WidthPreset.OneThird;
        var seen = new List<WidthPreset>();
        for (int i = 0; i < 5; i++)
        {
            seen.Add(p);
            p = p.Next();
        }
        Assert.Equal(
            [WidthPreset.OneThird, WidthPreset.Half, WidthPreset.TwoThirds, WidthPreset.Full, WidthPreset.OneThird],
            seen);
    }

    [Theory]
    [InlineData(WidthPreset.OneThird, 1d / 3d)]
    [InlineData(WidthPreset.Half, 0.5)]
    [InlineData(WidthPreset.TwoThirds, 2d / 3d)]
    [InlineData(WidthPreset.Full, 1d)]
    public void Fraction_MatchesPreset(WidthPreset preset, double expected)
    {
        Assert.Equal(expected, preset.Fraction(), 9);
    }
}
