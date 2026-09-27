using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using Ream.App.Editing;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

/// <summary>Where the Navigation Pane's search box looks.</summary>
public enum NavigationSearchScope
{
    CurrentNote,
    CurrentWorkspace,
    Everywhere,
}

/// <summary>One line in the Navigation Pane's heading list: a real paragraph in the focused note's live document.</summary>
public sealed class HeadingEntry(string text, int level, Paragraph paragraph)
{
    public string Text { get; } = text;
    public Paragraph Paragraph { get; } = paragraph;

    /// <summary>Indents Heading 1 the least and Title the most, matching <see cref="NoteStyles.HeadingLevelOf"/>'s own ranking.</summary>
    public Thickness Indent { get; } = new(Math.Max(0, level - 1) * 12, 0, 0, 0);
}

/// <summary>One line in the Navigation Pane's note list: a note in the current workspace.</summary>
public sealed class NoteEntry(NoteViewModel note)
{
    public NoteViewModel Note { get; } = note;
    public string Title => Note.DisplayTitle;
}

/// <summary>One line in the Navigation Pane's workspace list.</summary>
public sealed class WorkspaceEntry(WorkspaceViewModel workspace)
{
    public WorkspaceViewModel Workspace { get; } = workspace;
    public string Title => Workspace.DisplayName ?? "New workspace";
}

/// <summary>One search hit: which workspace and note it's in, and a snippet of the surrounding text.</summary>
public sealed class SearchResult(WorkspaceViewModel workspace, NoteViewModel note, string snippet)
{
    public WorkspaceViewModel Workspace { get; } = workspace;
    public NoteViewModel Note { get; } = note;
    public string Title => Note.DisplayTitle;
    public string Snippet { get; } = snippet;
}

/// <summary>
/// The Show group's Navigation Pane: the focused note's heading structure, the current workspace's notes, every
/// workspace, and a search across one of the three. Everything here is read-only summary state, rebuilt from
/// <see cref="AppViewModel"/> rather than owned by it - nothing here is persisted.
/// </summary>
public sealed partial class NavigationPaneViewModel : ObservableObject
{
    private const int SnippetRadius = 40;

    private readonly AppViewModel _app;
    private NoteViewModel? _trackedNote;

    public NavigationPaneViewModel(AppViewModel app)
    {
        _app = app;
        _app.PropertyChanged += OnAppPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    private string _searchText = "";

    [ObservableProperty]
    private NavigationSearchScope _searchScope = NavigationSearchScope.CurrentNote;

    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);

    public ObservableCollection<HeadingEntry> Headings { get; } = [];
    public ObservableCollection<NoteEntry> NotesInWorkspace { get; } = [];
    public ObservableCollection<WorkspaceEntry> WorkspaceList { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    partial void OnSearchTextChanged(string value) => RunSearch();
    partial void OnSearchScopeChanged(NavigationSearchScope value) => RunSearch();

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppViewModel.CurrentIndex)) return;
        RefreshWorkspaceLists();
        RefreshHeadings();
        RunSearch();
    }

    /// <summary>Rebuilds every list - called when the pane is opened, since it does nothing while closed.</summary>
    public void Refresh()
    {
        RefreshHeadings();
        RefreshWorkspaceLists();
        RunSearch();
    }

    private void RefreshHeadings()
    {
        var note = _app.CurrentWorkspace.FocusedNote;
        if (!ReferenceEquals(note, _trackedNote))
        {
            if (_trackedNote is not null) _trackedNote.PropertyChanged -= OnFocusedNoteChanged;
            _trackedNote = note;
            if (_trackedNote is not null) _trackedNote.PropertyChanged += OnFocusedNoteChanged;
        }

        Headings.Clear();
        if (note?.LiveDocument is not { } document) return;

        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph) continue;
            if (NoteStyles.HeadingLevelOf(paragraph, document) is not { } level) continue;

            string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            if (text.Length > 0) Headings.Add(new HeadingEntry(text, level, paragraph));
        }
    }

    private void OnFocusedNoteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == NoteViewModel.ContentChangedProperty) RefreshHeadings();
    }

    private void RefreshWorkspaceLists()
    {
        NotesInWorkspace.Clear();
        foreach (var note in _app.CurrentWorkspace.Notes) NotesInWorkspace.Add(new NoteEntry(note));

        // The two always-empty edges are skipped, same as anywhere else they'd just say "New workspace" twice over.
        WorkspaceList.Clear();
        foreach (var workspace in _app.Workspaces)
            if (workspace.Name is not null || !workspace.IsEmpty)
                WorkspaceList.Add(new WorkspaceEntry(workspace));
    }

    // ----- Jumping -----

    public void JumpToHeading(HeadingEntry entry) => _app.CurrentWorkspace.FocusedNote?.RequestCaretMove(entry.Paragraph);

    public void JumpToNote(NoteEntry entry)
    {
        int index = _app.CurrentWorkspace.Notes.IndexOf(entry.Note);
        if (index < 0) return;

        _app.CurrentWorkspace.SetFocus(index);
        _app.RequestEditorFocus();
    }

    public void JumpToWorkspace(WorkspaceEntry entry) => _app.SelectWorkspaceCommand.Execute(entry.Workspace);

    public void JumpToResult(SearchResult result)
    {
        if (!ReferenceEquals(result.Workspace, _app.CurrentWorkspace))
            _app.SelectWorkspaceCommand.Execute(result.Workspace);

        int index = result.Workspace.Notes.IndexOf(result.Note);
        if (index >= 0) result.Workspace.SetFocus(index);
        _app.RequestEditorFocus();
    }

    // ----- Search -----

    /// <summary>
    /// Plain, case-insensitive substring matching over each candidate note's title and saved text (flushed first,
    /// so an unsaved edit in the note you're looking at is still found). "Everywhere" reads every note's Body
    /// directly rather than through an editor, so it works for notes nothing has ever loaded.
    /// </summary>
    private void RunSearch()
    {
        SearchResults.Clear();
        if (!HasQuery) return;

        string query = SearchText.Trim();
        foreach (var (workspace, note) in NotesToSearch())
        {
            note.FlushDocument();
            string plain = NoteContent.ToPlainText(note.Body);
            int at = plain.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            bool titleMatch = note.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (at < 0 && !titleMatch) continue;

            string snippet = at >= 0 ? Snippet(plain, at, query.Length) : plain[..Math.Min(plain.Length, 80)];
            SearchResults.Add(new SearchResult(workspace, note, snippet));
        }
    }

    private static string Snippet(string text, int at, int matchLength)
    {
        int start = Math.Max(0, at - SnippetRadius);
        int end = Math.Min(text.Length, at + matchLength + SnippetRadius);
        string snippet = text[start..end].Replace('\r', ' ').Replace('\n', ' ').Trim();
        return (start > 0 ? "…" : "") + snippet + (end < text.Length ? "…" : "");
    }

    private IEnumerable<(WorkspaceViewModel Workspace, NoteViewModel Note)> NotesToSearch()
    {
        switch (SearchScope)
        {
            case NavigationSearchScope.CurrentNote:
                if (_app.CurrentWorkspace.FocusedNote is { } focused) yield return (_app.CurrentWorkspace, focused);
                break;
            case NavigationSearchScope.CurrentWorkspace:
                foreach (var note in _app.CurrentWorkspace.Notes) yield return (_app.CurrentWorkspace, note);
                break;
            default:
                foreach (var workspace in _app.Workspaces)
                    foreach (var note in workspace.Notes)
                        yield return (workspace, note);
                break;
        }
    }
}
