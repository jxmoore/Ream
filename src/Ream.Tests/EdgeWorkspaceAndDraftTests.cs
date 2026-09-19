using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class EdgeWorkspaceTests
{
    private static WorkspaceViewModel Workspace(string? name, int notes)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => new NoteViewModel { Title = $"note {i}" }), null);
        return workspace;
    }

    private static AppViewModel App(params WorkspaceViewModel[] workspaces) => new(new AppConfig(), workspaces);

    [Fact]
    public void AnEmptyWorkspaceSitsAboveTheFirstAndBelowTheLast()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));

        Assert.Equal(4, app.Workspaces.Count);
        Assert.True(app.Workspaces[0].IsEmpty);
        Assert.True(app.Workspaces[^1].IsEmpty);
        Assert.Equal([true, false, false, true], app.Workspaces.Select(w => w.IsEdge));
    }

    [Fact]
    public void TheAppOpensOnTheFirstRealWorkspace_NotTheEmptyOneAboveIt()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));

        Assert.Equal(1, app.CurrentIndex);
        Assert.Equal("A", app.CurrentWorkspace.Name);
    }

    [Fact]
    public void WithNoWorkspacesAtAll_ThereAreStillTwoEmptyOnes()
    {
        var app = App();

        Assert.Equal(2, app.Workspaces.Count);
        Assert.All(app.Workspaces, w => Assert.True(w.IsEmpty));
    }

    [Fact]
    public void AnEmptyNamedWorkspaceFirst_CountsAsTheLeadingOne()
    {
        var named = Workspace("Inbox", 0);
        var app = new AppViewModel(new AppConfig(), [named, Workspace("A", 1)], 1);

        Assert.Same(named, app.Workspaces[0]);
        Assert.Equal(3, app.Workspaces.Count);
        Assert.Equal("A", app.CurrentWorkspace.Name);
    }

    [Fact]
    public void EdgesAreLabelledWithAPlus_AndTheOthersAreNumberedFromOne()
    {
        var app = App(Workspace(null, 1), Workspace("Named", 1), Workspace(null, 1));

        Assert.Equal(["New workspace above", "Workspace 1", "Named", "Workspace 3", "New workspace below"], app.Workspaces.Select(w => w.MenuLabel));
    }

    [Fact]
    public void GoingUpFromTheFirstWorkspace_LandsOnTheEmptyOne_AndNoFurther()
    {
        var app = App(Workspace("A", 1));

        app.SwitchWorkspaceUpCommand.Execute(null);
        Assert.Equal(0, app.CurrentIndex);
        Assert.True(app.CurrentWorkspace.IsEmpty);

        app.SwitchWorkspaceUpCommand.Execute(null);
        Assert.Equal(0, app.CurrentIndex);
    }

    [Fact]
    public void PuttingANoteInTheEmptyWorkspaceAbove_AddsAnotherAboveIt_WithoutMovingTheView()
    {
        var app = App(Workspace("A", 1));
        app.CurrentIndex = 0;
        var leading = app.CurrentWorkspace;

        app.NewNoteCommand.Execute(null);

        Assert.Equal(4, app.Workspaces.Count);
        Assert.True(app.Workspaces[0].IsEmpty);
        Assert.Same(leading, app.CurrentWorkspace);
        Assert.Equal(1, app.CurrentIndex);
        Assert.Single(leading.Notes);
    }

    [Fact]
    public void TheStripSnapsInsteadOfSliding_WhenAWorkspaceAppearsAbove()
    {
        var app = App(Workspace("A", 1));
        app.CurrentIndex = 0;
        var suppression = new List<bool>();
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.SuppressAnimation)) suppression.Add(app.SuppressAnimation);
        };

        app.NewNoteCommand.Execute(null);

        Assert.Equal([true, false], suppression);
    }

    [Fact]
    public void MovingANoteIntoTheEmptyWorkspaceAbove_FollowsItThere()
    {
        var app = App(Workspace("A", 2));
        var note = app.CurrentWorkspace.Notes[0];

        app.MoveNoteToPrevWorkspaceCommand.Execute(null);

        Assert.Equal(4, app.Workspaces.Count);
        Assert.True(app.Workspaces[0].IsEmpty);
        Assert.Equal([note], app.CurrentWorkspace.Notes);
        Assert.Equal("New workspace above", app.Workspaces[0].MenuLabel);
        Assert.Equal(1, app.CurrentIndex);
    }

    [Fact]
    public void MovingANoteIntoTheEmptyWorkspaceBelow_AddsOneBelowIt()
    {
        var app = App(Workspace("A", 2));

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Equal(4, app.Workspaces.Count);
        Assert.Equal(2, app.CurrentIndex);
        Assert.Single(app.CurrentWorkspace.Notes);
        Assert.True(app.Workspaces[^1].IsEmpty);
    }

    [Fact]
    public void PruningLeavesBothEdgesAlone()
    {
        var app = App(Workspace("A", 1));
        app.CurrentIndex = 1;

        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Equal(3, app.Workspaces.Count);
        Assert.Equal(1, app.CurrentIndex);
    }

    [Fact]
    public void ClosingTheLastNote_LeavesAnEmptyWorkspaceThatPruningRemovesOnceYouLeave()
    {
        var app = App(Workspace(null, 1), Workspace("B", 1));
        app.CloseNoteCommand.Execute(null);
        Assert.Equal(4, app.Workspaces.Count);

        app.SwitchWorkspace(1);
        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Equal(["New workspace above", "B", "New workspace below"], app.Workspaces.Select(w => w.MenuLabel));
        Assert.Equal("B", app.CurrentWorkspace.Name);
    }

    // ----- What gets saved -----

    [Fact]
    public void TheEdgeWorkspaces_AreNotSaved()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));

        Assert.Equal(["A", "B"], SnapshotMapper.ToSnapshot(app).Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void ANamedEmptyWorkspaceAtTheTop_IsSaved()
    {
        var app = App(Workspace("A", 1));
        app.Workspaces[0].Name = "Inbox";

        Assert.Equal(["Inbox", "A"], SnapshotMapper.ToSnapshot(app).Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void LoadingWhatWasSaved_RestoresTheEdges_AndTheCurrentWorkspace()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));
        app.SwitchWorkspace(1);
        var snapshot = SnapshotMapper.ToSnapshot(app);

        var reloaded = SnapshotMapper.ToViewModel(snapshot, new AppConfig(), null!);

        Assert.Equal(4, reloaded.Workspaces.Count);
        Assert.Equal("B", reloaded.CurrentWorkspace.Name);
        Assert.True(reloaded.Workspaces[0].IsEmpty && reloaded.Workspaces[^1].IsEmpty);
    }

    [Fact]
    public void LoadingWithNoRememberedWorkspace_StartsOnTheFirstRealOne()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));
        var snapshot = SnapshotMapper.ToSnapshot(app) with { CurrentWorkspaceId = null };

        var reloaded = SnapshotMapper.ToViewModel(snapshot, new AppConfig(), null!);

        Assert.Equal("A", reloaded.CurrentWorkspace.Name);
    }
}

public class DraftNoteTests
{
    private const string Text = "<ReamNote schemaVersion=\"1\"><Doc><P><R>hello</R></P></Doc></ReamNote>";
    private const string Empty = "<ReamNote schemaVersion=\"1\"><Doc><P><R> </R></P></Doc></ReamNote>";

    private static WorkspaceViewModel Workspace(string? name, int notes)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => new NoteViewModel { Title = $"note {i}" }), null);
        return workspace;
    }

    private static AppViewModel App(params WorkspaceViewModel[] workspaces) => new(new AppConfig(), workspaces);

    // ----- Making them -----

    [Fact]
    public void GoingRightFromTheLastNote_OpensABlankDraftThere()
    {
        var app = App(Workspace("A", 2));
        app.CurrentWorkspace.SetFocus(1);

        app.FocusNextNoteCommand.Execute(null);

        var notes = app.CurrentWorkspace.Notes;
        Assert.Equal(3, notes.Count);
        Assert.Equal(2, app.CurrentWorkspace.FocusedIndex);
        Assert.True(notes[2].IsDraft);
        Assert.True(notes[2].IsFocused);
    }

    [Fact]
    public void GoingLeftFromTheFirstNote_OpensABlankDraftBeforeIt()
    {
        var app = App(Workspace("A", 2));
        var first = app.CurrentWorkspace.Notes[0];

        app.FocusPrevNoteCommand.Execute(null);

        var notes = app.CurrentWorkspace.Notes;
        Assert.Equal(3, notes.Count);
        Assert.Equal(0, app.CurrentWorkspace.FocusedIndex);
        Assert.True(notes[0].IsDraft);
        Assert.Same(first, notes[1]);
    }

    [Fact]
    public void InTheMiddleOfARow_TheArrowsJustMoveFocus()
    {
        var app = App(Workspace("A", 3));

        app.FocusNextNoteCommand.Execute(null);
        app.FocusPrevNoteCommand.Execute(null);

        Assert.Equal(3, app.CurrentWorkspace.Notes.Count);
        Assert.Equal(0, app.CurrentWorkspace.FocusedIndex);
    }

    [Fact]
    public void ADraftIsNotStackedOnADraft()
    {
        var app = App(Workspace("A", 1));

        app.FocusNextNoteCommand.Execute(null);
        app.FocusNextNoteCommand.Execute(null);
        app.NewNoteCommand.Execute(null);

        Assert.Equal(2, app.CurrentWorkspace.Notes.Count);
    }

    [Fact]
    public void NewNoteMakesADraftToo()
    {
        var app = App(Workspace("A", 2));

        app.NewNoteCommand.Execute(null);

        var notes = app.CurrentWorkspace.Notes;
        Assert.Equal(3, notes.Count);
        Assert.True(notes[1].IsDraft);
        Assert.Equal(1, app.CurrentWorkspace.FocusedIndex);
    }

    [Fact]
    public void AnEmptyWorkspace_GetsADraftFromEitherArrow()
    {
        var right = App(Workspace("A", 1));
        right.CurrentIndex = 0;
        right.FocusNextNoteCommand.Execute(null);
        Assert.Single(right.CurrentWorkspace.Notes);

        var left = App(Workspace("A", 1));
        left.CurrentIndex = 0;
        left.FocusPrevNoteCommand.Execute(null);
        Assert.Single(left.CurrentWorkspace.Notes);
    }

    [Fact]
    public void TheScrollWheelsStopAtTheEdge_TheyNeverMakeNotes()
    {
        var app = App(Workspace("A", 2));

        app.FocusNoteBy(5);
        app.FocusNoteBy(-5);

        Assert.Equal(2, app.CurrentWorkspace.Notes.Count);
    }

    [Fact]
    public void AskingForTheEditor_HappensWhetherOrNotADraftWasMade()
    {
        var app = App(Workspace("A", 1));
        int requests = 0;
        app.FocusEditorRequested += () => requests++;

        app.FocusNextNoteCommand.Execute(null);
        app.FocusNextNoteCommand.Execute(null);

        Assert.Equal(2, requests);
    }

    // ----- Leaving them blank -----

    [Fact]
    public void ABlankDraftVanishes_WhenFocusMovesLeft()
    {
        var app = App(Workspace("A", 2));
        app.CurrentWorkspace.SetFocus(1);
        app.FocusNextNoteCommand.Execute(null);

        app.FocusPrevNoteCommand.Execute(null);

        var workspace = app.CurrentWorkspace;
        Assert.Equal(2, workspace.Notes.Count);
        Assert.Equal(1, workspace.FocusedIndex);
        Assert.Same(workspace.Notes[1], workspace.FocusedNote);
        Assert.True(workspace.Notes[1].IsFocused);
    }

    [Fact]
    public void ABlankDraftBeforeTheFirstNote_VanishesAndFocusStaysOnTheRightNote()
    {
        var app = App(Workspace("A", 2));
        var first = app.CurrentWorkspace.Notes[0];
        app.FocusPrevNoteCommand.Execute(null);

        app.FocusNextNoteCommand.Execute(null);

        var workspace = app.CurrentWorkspace;
        Assert.Equal(2, workspace.Notes.Count);
        Assert.Same(first, workspace.FocusedNote);
        Assert.Equal(0, workspace.FocusedIndex);
    }

    [Fact]
    public void ABlankDraftVanishes_WhenYouClickAnotherNote()
    {
        var app = App(Workspace("A", 2));
        var first = app.CurrentWorkspace.Notes[0];
        app.NewNoteCommand.Execute(null);

        first.FocusCommand.Execute(null);

        Assert.Equal(2, app.CurrentWorkspace.Notes.Count);
        Assert.Same(first, app.CurrentWorkspace.FocusedNote);
    }

    [Fact]
    public void ABlankDraftVanishes_WhenYouLeaveTheWorkspace()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));
        var a = app.CurrentWorkspace;
        app.NewNoteCommand.Execute(null);
        Assert.Equal(2, a.Notes.Count);

        app.SwitchWorkspace(1);

        Assert.Single(a.Notes);
    }

    [Fact]
    public void AWorkspaceLeftEmptyBecauseItsDraftVanished_IsPrunedOnceYouAreClearOfIt()
    {
        var app = App(Workspace("A", 1));
        app.CurrentIndex = 2; // the empty workspace below
        app.NewNoteCommand.Execute(null);
        Assert.Equal(4, app.Workspaces.Count);

        app.SwitchWorkspace(-1);
        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Equal(3, app.Workspaces.Count);
        Assert.Equal("A", app.CurrentWorkspace.Name);
    }

    [Fact]
    public void ARealNoteLeftBlankLater_IsNotDeleted()
    {
        var app = App(Workspace("A", 2));
        var second = app.CurrentWorkspace.Notes[1];
        second.Body = Empty; // an ordinary note, not a draft

        app.CurrentWorkspace.SetFocus(1);
        app.FocusPrevNoteCommand.Execute(null);

        Assert.Equal(2, app.CurrentWorkspace.Notes.Count);
    }

    // ----- Keeping them -----

    [Fact]
    public void ADraftWithText_BecomesARealNote_AndStays()
    {
        var app = App(Workspace("A", 1));
        app.NewNoteCommand.Execute(null);
        var draft = app.CurrentWorkspace.FocusedNote!;

        draft.Body = Text;
        app.FocusPrevNoteCommand.Execute(null);

        Assert.Equal(2, app.CurrentWorkspace.Notes.Count);
        Assert.False(draft.IsDraft);
    }

    [Fact]
    public void ADraftWithOnlySpaces_IsStillBlank()
    {
        var app = App(Workspace("A", 1));
        app.NewNoteCommand.Execute(null);
        app.CurrentWorkspace.FocusedNote!.Body = Empty;

        app.FocusPrevNoteCommand.Execute(null);

        Assert.Single(app.CurrentWorkspace.Notes);
    }

    [Fact]
    public void ADraftMovedToAnotherWorkspace_IsStillADraft_AndStaysWhileItIsFocused()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));
        app.NewNoteCommand.Execute(null);
        var draft = app.CurrentWorkspace.FocusedNote!;

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Equal("B", app.CurrentWorkspace.Name);
        Assert.Contains(draft, app.CurrentWorkspace.Notes);
        Assert.True(draft.IsDraft);
    }

    // ----- Saving -----

    [Fact]
    public void ABlankDraft_IsNotSaved()
    {
        var app = App(Workspace("A", 1));
        app.NewNoteCommand.Execute(null);

        var saved = SnapshotMapper.ToSnapshot(app).Workspaces.Single();

        Assert.Single(saved.Notes);
        Assert.Null(saved.FocusedNoteId); // the focused note is the unsaved draft
    }

    [Fact]
    public void ADraftWithText_IsSaved_AndNoLongerADraft()
    {
        var app = App(Workspace("A", 1));
        app.NewNoteCommand.Execute(null);
        var draft = app.CurrentWorkspace.FocusedNote!;
        draft.Body = Text;

        var saved = SnapshotMapper.ToSnapshot(app).Workspaces.Single();

        Assert.Equal(2, saved.Notes.Count);
        Assert.Equal(draft.Id, saved.FocusedNoteId);
        Assert.False(draft.IsDraft);
    }

    [Fact]
    public void AWorkspaceHoldingOnlyABlankDraft_IsNotSaved()
    {
        var app = App(Workspace("A", 1));
        app.CurrentIndex = 0;
        app.NewNoteCommand.Execute(null);

        Assert.Equal(["A"], SnapshotMapper.ToSnapshot(app).Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void ANamedWorkspaceHoldingOnlyABlankDraft_IsSavedEmpty()
    {
        var app = App(Workspace("A", 1));
        app.CurrentIndex = 0;
        app.CurrentWorkspace.Name = "Ideas";
        app.NewNoteCommand.Execute(null);

        var saved = SnapshotMapper.ToSnapshot(app).Workspaces;

        Assert.Equal(["Ideas", "A"], saved.Select(w => w.Name));
        Assert.Empty(saved[0].Notes);
    }
}

public class NoteBlankTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData("<ReamNote schemaVersion=\"1\"><Doc><P /></Doc></ReamNote>")]
    [InlineData("<ReamNote schemaVersion=\"1\"><Doc><P><R>  </R></P></Doc></ReamNote>")]
    [InlineData("<ReamNote schemaVersion=\"1\"><Doc><P><R></R></P><UL><LI><P><R> </R></P></LI></UL></Doc></ReamNote>")]
    public void NothingVisible_IsBlank(string content)
    {
        Assert.True(NoteContent.IsBlank(content));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("<ReamNote schemaVersion=\"1\"><Doc><P><R>hello</R></P></Doc></ReamNote>")]
    [InlineData("<ReamNote schemaVersion=\"1\"><Doc><P><IMG src=\"a.png\" /></P></Doc></ReamNote>")]
    [InlineData("<ReamNote schemaVersion=\"1\"><Doc><UL><LI><P><R>item</R></P></LI></UL></Doc></ReamNote>")]
    public void AnythingVisible_IsNotBlank(string content)
    {
        Assert.False(NoteContent.IsBlank(content));
    }

    [Fact]
    public void UnreadableNoteXml_IsNeverTreatedAsBlank()
    {
        Assert.False(NoteContent.IsBlank("<ReamNote><Doc><P>oops"));
    }

    [Fact]
    public void ADraftTypedInto_LosesItsDraftStatusOnTheFirstSave() => Ui.Run(() =>
    {
        var note = new Ream.App.ViewModels.NoteViewModel { IsDraft = true };
        var document = note.OpenDocument();
        Assert.True(note.IsBlankDraft());

        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("typed")));
        note.NotifyContentChanged();

        Assert.False(note.IsBlankDraft());
        Assert.False(note.IsDraft);
        Assert.Equal("typed", note.Title);
    });

    [Fact]
    public void ADraftTypedInAndThenEmptiedAgain_IsBlankAgain() => Ui.Run(() =>
    {
        var note = new Ream.App.ViewModels.NoteViewModel { IsDraft = true };
        var document = note.OpenDocument();

        document.Blocks.Clear();
        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("   ")));
        note.NotifyContentChanged();

        Assert.True(note.IsBlankDraft());
    });
}

public class DraftInTheWindowTests
{
    [Fact]
    public void ADraftMadeByArrowingPastTheEnd_GetsAColumn_AndTheKeyboardFocus() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        fx.App.CurrentWorkspace.SetFocus(1);

        fx.App.FocusNextNoteCommand.Execute(null);
        Ui.Settle();

        var draft = fx.App.CurrentWorkspace.FocusedNote!;
        var column = fx.ColumnOf(draft);
        Assert.True(draft.IsDraft);
        Assert.Same(column.Editor, System.Windows.Input.FocusManager.GetFocusedElement(fx.Window));
    });

    [Fact]
    public void ABlankDraftLeftBehind_LosesItsColumn() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        fx.App.CurrentWorkspace.SetFocus(1);
        fx.App.FocusNextNoteCommand.Execute(null);
        Ui.Settle();
        var draft = fx.App.CurrentWorkspace.FocusedNote!;

        fx.App.FocusPrevNoteCommand.Execute(null);
        Ui.Settle();

        Assert.DoesNotContain(fx.Columns, c => ReferenceEquals(c.DataContext, draft));
        Assert.Equal(2, fx.Columns.Count());
    });

    [Fact]
    public void ATypedDraft_IsSavedByTheSnapshot_AndABlankOneIsNot() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        fx.App.NewNoteCommand.Execute(null);
        Ui.Settle();
        var draft = fx.App.CurrentWorkspace.FocusedNote!;
        Assert.Single(Ream.App.ViewModels.SnapshotMapper.ToSnapshot(fx.App).Workspaces.Single().Notes);

        var editor = fx.ColumnOf(draft).Editor;
        editor.AppendText("something worth keeping");
        Ui.Settle();

        var saved = Ream.App.ViewModels.SnapshotMapper.ToSnapshot(fx.App).Workspaces.Single().Notes;
        Assert.Equal(2, saved.Count);
        Assert.Equal("something worth keeping", saved[1].Title);
    });
}
