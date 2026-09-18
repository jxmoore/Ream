using Ream.Core.Layout;

namespace Ream.Tests;

public class RowLayoutTests
{
    private const double W = 1000;
    private const double Gap = 20;

    [Fact]
    public void FullColumn_FillsViewportMinusOuterGaps()
    {
        Assert.Equal(W - 2 * Gap, RowLayout.ColumnWidth(1, W, Gap), 6);
    }

    [Fact]
    public void TwoHalves_ExactlyFillViewport()
    {
        double w = RowLayout.ColumnWidth(0.5, W, Gap);
        Assert.Equal(W, 2 * w + 3 * Gap, 6);
    }

    [Fact]
    public void ThreeThirds_ExactlyFillViewport()
    {
        double w = RowLayout.ColumnWidth(1d / 3d, W, Gap);
        Assert.Equal(W, 3 * w + 4 * Gap, 6);
    }

    [Fact]
    public void Lefts_AccountForLeadingAndInnerGaps()
    {
        var lefts = RowLayout.Lefts([100, 200, 50], 10);
        Assert.Equal([10d, 120d, 330d], lefts);
        Assert.Equal(390d, RowLayout.ContentWidth([100, 200, 50], 10), 6);
    }

    [Fact]
    public void EmptyRow_HasZeroContent()
    {
        Assert.Equal(0, RowLayout.ContentWidth([], Gap));
    }

    private static (double[] widths, double[] lefts, double content) ThreeHalves()
    {
        double w = RowLayout.ColumnWidth(0.5, W, Gap);
        double[] widths = [w, w, w];
        return (widths, RowLayout.Lefts(widths, Gap), RowLayout.ContentWidth(widths, Gap));
    }

    [Fact]
    public void Minimal_AlreadyVisible_DoesNotMove()
    {
        var (widths, lefts, content) = ThreeHalves();
        double offset = RowLayout.TargetOffset(0, lefts[1], widths[1], W, Gap, content, false);
        Assert.Equal(0, offset, 6);
    }

    [Fact]
    public void Minimal_OffscreenRight_ScrollsJustEnough()
    {
        var (widths, lefts, content) = ThreeHalves();
        double offset = RowLayout.TargetOffset(0, lefts[2], widths[2], W, Gap, content, false);
        Assert.Equal(lefts[2] + widths[2] + Gap - W, offset, 6);
    }

    [Fact]
    public void Minimal_OffscreenLeft_ScrollsBackToFocusedColumn()
    {
        var (widths, lefts, content) = ThreeHalves();
        double offset = RowLayout.TargetOffset(490, lefts[0], widths[0], W, Gap, content, false);
        Assert.Equal(0, offset, 6);
    }

    [Fact]
    public void Minimal_NeverLeavesBlankSpaceAfterLastColumn()
    {
        double w = RowLayout.ColumnWidth(0.5, W, Gap);
        double[] widths = [w];
        double offset = RowLayout.TargetOffset(300, Gap, w, W, Gap, RowLayout.ContentWidth(widths, Gap), false);
        Assert.Equal(0, offset, 6);
    }

    [Fact]
    public void Centered_PutsFocusedColumnInTheMiddle()
    {
        var (widths, lefts, content) = ThreeHalves();
        double offset = RowLayout.TargetOffset(0, lefts[1], widths[1], W, Gap, content, true);
        Assert.Equal(lefts[1] + widths[1] / 2 - W / 2, offset, 6);
    }

    [Fact]
    public void Centered_FirstColumn_MayScrollBeforeContentStart()
    {
        var (widths, lefts, content) = ThreeHalves();
        double offset = RowLayout.TargetOffset(0, lefts[0], widths[0], W, Gap, content, true);
        Assert.True(offset < 0);
    }
}
