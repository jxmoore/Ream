using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Ream.App.Editing;

/// <summary>Plain-text search and replace over a <see cref="RichTextBox"/>'s <see cref="FlowDocument"/>.</summary>
internal static class DocumentSearch
{
    internal readonly record struct Match(TextPointer Start, TextPointer End);

    /// <summary>
    /// The next occurrence of <paramref name="query"/> at or after <paramref name="from"/>; wraps to the
    /// document start (and can land back on <paramref name="from"/> itself) if nothing comes after it.
    /// Null when the query is empty or does not occur anywhere in the document.
    /// </summary>
    internal static Match? FindNext(RichTextBox editor, string query, bool matchCase, TextPointer from)
    {
        if (string.IsNullOrEmpty(query)) return null;

        return Search(from, editor.Document.ContentEnd, query, matchCase)
            ?? Search(editor.Document.ContentStart, editor.Document.ContentEnd, query, matchCase);
    }

    /// <summary>Replaces every occurrence of <paramref name="query"/> with <paramref name="replacement"/>; returns how many were replaced.</summary>
    internal static int ReplaceAll(RichTextBox editor, string query, string replacement, bool matchCase)
    {
        if (string.IsNullOrEmpty(query)) return 0;

        int count = 0;
        var cursor = editor.Document.ContentStart;
        while (Search(cursor, editor.Document.ContentEnd, query, matchCase) is { } match)
        {
            var range = new TextRange(match.Start, match.End);
            range.Text = replacement;
            count++;
            cursor = range.End; // resume after the replacement, so a replacement containing the query can't loop forever
        }
        return count;
    }

    /// <summary>
    /// A plain-text scan from <paramref name="start"/> to <paramref name="end"/>: flattens the run text into a
    /// string (a naive per-run search would miss a query that straddles two differently-formatted runs) with a
    /// parallel list of the TextPointer before each character, so a match's offset maps back to real positions.
    /// </summary>
    private static Match? Search(TextPointer start, TextPointer end, string query, bool matchCase)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = new StringBuilder();
        var pointers = new List<TextPointer>();

        for (var nav = start; nav is not null && nav.CompareTo(end) < 0;)
        {
            if (nav.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                pointers.Add(nav);
                text.Append(nav.GetTextInRun(LogicalDirection.Forward)[0]);
                nav = nav.GetPositionAtOffset(1, LogicalDirection.Forward);
            }
            else
            {
                nav = nav.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        int index = text.ToString().IndexOf(query, comparison);
        if (index < 0) return null;

        // Not pointers[index + query.Length] (which may not exist) or `end` (which, right at a paragraph's
        // own end, makes TextRange.Text pick up its trailing "\r\n"): one past the last matched character.
        var matchStart = pointers[index];
        var matchEnd = pointers[index + query.Length - 1].GetPositionAtOffset(1, LogicalDirection.Forward)!;
        return new Match(matchStart, matchEnd);
    }
}
