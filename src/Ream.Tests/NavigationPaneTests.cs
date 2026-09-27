using System.Windows.Documents;
using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class NavigationPaneTests
{
    private const string HeadingsBody = """
        <ReamNote schemaVersion="1"><Doc>
        <P size="28" b="1"><R>Chapter One</R></P>
        <P><R>Some text.</R></P>
        <P size="22" b="1"><R>Section A</R></P>
        <P><R>More text.</R></P>
        </Doc></ReamNote>
        """;

    private static (AppViewModel App, NoteViewModel Note) FixtureWithHeadings()
    {
        var workspace = new WorkspaceViewModel("W");
        var note = new NoteViewModel { Title = "Untitled", Body = HeadingsBody };
        workspace.LoadNotes([note], note.Id);
        note.OpenDocument(); // simulates NoteColumnView.EnsureLoaded, without needing a real editor

        var app = new AppViewModel(new AppConfig(), [workspace]);
        return (app, note);
    }

    [Fact]
    public void Headings_ReflectTheFocusedNotesRealHeadings() => Ui.Run(() =>
    {
        var (app, _) = FixtureWithHeadings();

        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();

        Assert.Equal(["Chapter One", "Section A"], pane.Headings.Select(h => h.Text));
    });

    [Fact]
    public void Headings_UpdateAsTheNoteIsEdited() => Ui.Run(() =>
    {
        var (app, note) = FixtureWithHeadings();
        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();
        Assert.Equal(2, pane.Headings.Count);

        // Simulate a further edit: NotifyContentChanged is what NoteColumnView's TextChanged handler raises.
        note.NotifyContentChanged();

        Assert.Equal(2, pane.Headings.Count); // still tracking correctly, no crash on re-scan
    });

    [Fact]
    public void JumpToHeading_RaisesTheNotesCaretMoveEvent() => Ui.Run(() =>
    {
        var (app, note) = FixtureWithHeadings();
        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();

        Paragraph? landed = null;
        note.CaretMoveRequested += p => landed = p;

        pane.JumpToHeading(pane.Headings[1]);

        Assert.NotNull(landed);
        Assert.Same(pane.Headings[1].Paragraph, landed);
    });

    [Fact]
    public void NotesInWorkspace_ListsEveryNoteInTheCurrentWorkspace() => Ui.Run(() =>
    {
        var workspace = new WorkspaceViewModel("W");
        var a = new NoteViewModel { Title = "First" };
        var b = new NoteViewModel { Title = "Second" };
        workspace.LoadNotes([a, b], a.Id);
        var app = new AppViewModel(new AppConfig(), [workspace]);

        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();

        Assert.Equal(["First", "Second"], pane.NotesInWorkspace.Select(n => n.Title));
    });

    [Fact]
    public void JumpToNote_FocusesItInTheCurrentWorkspace() => Ui.Run(() =>
    {
        var workspace = new WorkspaceViewModel("W");
        var a = new NoteViewModel { Title = "First" };
        var b = new NoteViewModel { Title = "Second" };
        workspace.LoadNotes([a, b], a.Id);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();

        pane.JumpToNote(pane.NotesInWorkspace[1]);

        Assert.Same(b, app.CurrentWorkspace.FocusedNote);
    });

    [Fact]
    public void WorkspaceList_SkipsTheAlwaysEmptyEdges_ButKeepsNamedOrOccupiedOnes() => Ui.Run(() =>
    {
        var w1 = new WorkspaceViewModel("Alpha");
        w1.LoadNotes([new NoteViewModel()], null);
        var w2 = new WorkspaceViewModel("Beta");
        w2.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [w1, w2]); // edge workspaces are added automatically

        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();

        Assert.Equal(["Alpha", "Beta"], pane.WorkspaceList.Select(w => w.Title));
    });

    [Fact]
    public void JumpToWorkspace_SwitchesTheCurrentWorkspace() => Ui.Run(() =>
    {
        var w1 = new WorkspaceViewModel("Alpha");
        w1.LoadNotes([new NoteViewModel()], null);
        var w2 = new WorkspaceViewModel("Beta");
        w2.LoadNotes([new NoteViewModel()], null);
        var app = new AppViewModel(new AppConfig(), [w1, w2], currentIndex: 0);
        var pane = new NavigationPaneViewModel(app);
        pane.Refresh();

        pane.JumpToWorkspace(pane.WorkspaceList.Single(w => w.Title == "Beta"));

        Assert.Same(w2, app.CurrentWorkspace);
    });

    // ----- Search -----

    private static AppViewModel SearchFixture(out WorkspaceViewModel other)
    {
        var current = new WorkspaceViewModel("Current");
        var focused = new NoteViewModel { Title = "Focused note", Body = "the quick brown fox" };
        var sibling = new NoteViewModel { Title = "Sibling note", Body = "jumps over the lazy dog" };
        current.LoadNotes([focused, sibling], focused.Id);

        other = new WorkspaceViewModel("Elsewhere");
        var elsewhereNote = new NoteViewModel { Title = "Elsewhere note", Body = "a needle in a haystack" };
        other.LoadNotes([elsewhereNote], null);

        return new AppViewModel(new AppConfig(), [current, other], currentIndex: 0);
    }

    [Fact]
    public void Search_CurrentNoteScope_OnlyMatchesTheFocusedNote() => Ui.Run(() =>
    {
        var app = SearchFixture(out _);
        var pane = new NavigationPaneViewModel(app) { SearchScope = NavigationSearchScope.CurrentNote };

        pane.SearchText = "fox";
        Assert.Single(pane.SearchResults);

        pane.SearchText = "lazy"; // in the sibling note, not the focused one
        Assert.Empty(pane.SearchResults);
    });

    [Fact]
    public void Search_CurrentWorkspaceScope_MatchesAnyNoteInThatWorkspace_NotOtherWorkspaces() => Ui.Run(() =>
    {
        var app = SearchFixture(out _);
        var pane = new NavigationPaneViewModel(app) { SearchScope = NavigationSearchScope.CurrentWorkspace };

        pane.SearchText = "lazy";
        Assert.Single(pane.SearchResults);

        pane.SearchText = "needle"; // only in the other workspace
        Assert.Empty(pane.SearchResults);
    });

    [Fact]
    public void Search_EverywhereScope_MatchesAcrossWorkspaces() => Ui.Run(() =>
    {
        var app = SearchFixture(out var other);
        var pane = new NavigationPaneViewModel(app) { SearchScope = NavigationSearchScope.Everywhere };

        pane.SearchText = "needle";

        var result = Assert.Single(pane.SearchResults);
        Assert.Same(other, result.Workspace);
        Assert.Equal("Elsewhere note", result.Title);
    });

    [Fact]
    public void Search_IsCaseInsensitive_AndMatchesTheTitleToo() => Ui.Run(() =>
    {
        var app = SearchFixture(out _);
        var pane = new NavigationPaneViewModel(app) { SearchScope = NavigationSearchScope.CurrentWorkspace };

        pane.SearchText = "FOCUSED NOTE"; // the title, not the body

        Assert.Contains(pane.SearchResults, r => r.Title == "Focused note");
    });

    [Fact]
    public void ClearingTheSearchText_ClearsTheResults() => Ui.Run(() =>
    {
        var app = SearchFixture(out _);
        var pane = new NavigationPaneViewModel(app) { SearchScope = NavigationSearchScope.CurrentWorkspace };
        pane.SearchText = "fox";
        Assert.True(pane.HasQuery);

        pane.SearchText = "";

        Assert.False(pane.HasQuery);
        Assert.Empty(pane.SearchResults);
    });
}
