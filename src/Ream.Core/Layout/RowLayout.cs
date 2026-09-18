namespace Ream.Core.Layout;

/// <summary>Pure geometry for a horizontal row of note columns (niri-style).</summary>
public static class RowLayout
{
    // A column of proportion p is p of the viewport minus gaps, so that
    // 1/2 + 1/2 (or 1/3 x 3) exactly fill the viewport including the outer gaps.
    public static double ColumnWidth(double fraction, double viewportWidth, double gap) =>
        Math.Max(0, fraction * (viewportWidth - gap) - gap);

    public static double[] Lefts(IReadOnlyList<double> widths, double gap)
    {
        var lefts = new double[widths.Count];
        double x = gap;
        for (int i = 0; i < widths.Count; i++)
        {
            lefts[i] = x;
            x += widths[i] + gap;
        }
        return lefts;
    }

    public static double ContentWidth(IReadOnlyList<double> widths, double gap)
    {
        if (widths.Count == 0) return 0;
        double total = gap;
        foreach (double w in widths) total += w + gap;
        return total;
    }

    /// <summary>
    /// The scroll offset that brings the focused column into view.
    /// Default is minimal scroll (do nothing if already visible); centered mode
    /// always centers the column and may scroll past either end.
    /// </summary>
    public static double TargetOffset(
        double currentOffset,
        double left,
        double width,
        double viewportWidth,
        double gap,
        double contentWidth,
        bool centerFocused)
    {
        if (centerFocused)
            return left + width / 2 - viewportWidth / 2;

        double offset = currentOffset;
        if (left - gap < offset)
            offset = left - gap;
        else if (left + width + gap > offset + viewportWidth)
            offset = left + width + gap - viewportWidth;

        double max = Math.Max(0, contentWidth - viewportWidth);
        return Math.Clamp(offset, 0, max);
    }
}
