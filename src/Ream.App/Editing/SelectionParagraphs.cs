using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Ream.App.Editing;

/// <summary>The paragraphs a selection touches (the caret's paragraph when nothing is selected), in document order.</summary>
internal static class SelectionParagraphs
{
    public static List<Paragraph> Of(RichTextBox editor)
    {
        var found = new List<Paragraph>();
        var end = editor.Selection.End;

        for (var p = editor.Selection.Start; p is not null && p.CompareTo(end) <= 0; p = p.GetNextInsertionPosition(LogicalDirection.Forward))
        {
            if (p.Paragraph is { } paragraph && !found.Contains(paragraph)) found.Add(paragraph);
        }

        if (found.Count == 0 && editor.CaretPosition.Paragraph is { } caretParagraph) found.Add(caretParagraph);
        return found;
    }
}
