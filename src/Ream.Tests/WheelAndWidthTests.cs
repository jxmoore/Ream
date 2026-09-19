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

