using System.Windows;
using System.Windows.Documents;

namespace Ream.App.Editing;

/// <summary>
/// The named paragraph styles the Styles gallery applies (Normal, Heading 1-4, Title, Subtitle, Quote) - shared with
/// anything else that needs to recognize one: Outline view (which paragraphs are headings) and the Navigation Pane's
/// heading list. A paragraph "is" a style when its size, weight and slant all match exactly, the same rule
/// <c>RibbonView.Refresh()</c> uses to light up the gallery tile.
/// </summary>
internal static class NoteStyles
{
    public static readonly (string Label, double? Size, FontWeight Weight, FontStyle Style)[] All =
    [
        ("Normal", null, FontWeights.Normal, FontStyles.Normal),
        ("Heading 1", 28, FontWeights.Bold, FontStyles.Normal),
        ("Heading 2", 22, FontWeights.Bold, FontStyles.Normal),
        ("Heading 3", 18, FontWeights.Bold, FontStyles.Normal),
        ("Heading 4", 15, FontWeights.Bold, FontStyles.Normal),
        ("Title", 34, FontWeights.Bold, FontStyles.Normal),
        ("Subtitle", 18, FontWeights.Normal, FontStyles.Italic),
        ("Quote", 14, FontWeights.Normal, FontStyles.Italic),
    ];

    /// <summary>Indices into <see cref="All"/> that count as a heading (Outline view, the Navigation Pane) - Heading 1-4 and Title, not Normal, Subtitle or Quote.</summary>
    public static readonly int[] HeadingIndices = [1, 2, 3, 4, 5];

    /// <summary>The index into <see cref="All"/> this paragraph exactly matches, or -1. "Normal" is compared against the document's own defaults.</summary>
    public static int StyleOf(Paragraph paragraph, FlowDocument document)
    {
        for (int i = 0; i < All.Length; i++)
        {
            var (_, size, weight, style) = All[i];
            double expected = size ?? document.FontSize;
            if (Math.Abs(paragraph.FontSize - expected) < 0.5 && paragraph.FontWeight == weight && paragraph.FontStyle == style)
                return i;
        }
        return -1;
    }

    /// <summary>1-5 for Heading 1-4 / Title (higher = a smaller, lower-ranked heading, matching Word's outline levels), or null.</summary>
    public static int? HeadingLevelOf(Paragraph paragraph, FlowDocument document)
    {
        int index = StyleOf(paragraph, document);
        return Array.IndexOf(HeadingIndices, index) >= 0 ? index : null;
    }
}
