using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.Editing;

namespace Ream.App.Views;

/// <summary>Ream's Find / Find and Replace window: modeless, so the note stays interactive behind it.</summary>
internal sealed partial class FindReplaceWindow : Window
{
    private readonly RichTextBox _editor;

    public FindReplaceWindow(RichTextBox editor, bool withReplace)
    {
        InitializeComponent();
        _editor = editor;

        SetMode(withReplace);
        Loaded += (_, _) => FocusFindBox();
    }

    private bool MatchCase => MatchCaseCheckBox.IsChecked == true;

    /// <summary>Switches an already-open window between Find and Find and Replace (Ctrl+F / Ctrl+H reuse the same open window).</summary>
    internal void SetMode(bool withReplace)
    {
        Title = withReplace ? "Replace" : "Find";
        ReplaceLabel.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceBox.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButton.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceAllButton.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "";
        FocusFindBox();
    }

    private void FocusFindBox()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) FindNext();
    }

    private void OnFindNext(object sender, RoutedEventArgs e) => FindNext();

    private void FindNext()
    {
        var match = DocumentSearch.FindNext(_editor, FindBox.Text, MatchCase, _editor.Selection.End);
        ShowMatch(match);
    }

    private void ShowMatch(DocumentSearch.Match? match)
    {
        if (match is null)
        {
            StatusText.Text = string.IsNullOrEmpty(FindBox.Text) ? "" : "Phrase not found.";
            return;
        }

        StatusText.Text = "";
        _editor.Selection.Select(match.Value.Start, match.Value.End);
        match.Value.Start.Paragraph?.BringIntoView();
    }

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        bool selectionMatches = !_editor.Selection.IsEmpty
            && string.Equals(_editor.Selection.Text, FindBox.Text, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        if (selectionMatches) _editor.Selection.Text = ReplaceBox.Text;
        FindNext();
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        int count = DocumentSearch.ReplaceAll(_editor, FindBox.Text, ReplaceBox.Text, MatchCase);
        StatusText.Text = count switch
        {
            0 => "Phrase not found.",
            1 => "1 replacement made.",
            _ => $"{count} replacements made.",
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
