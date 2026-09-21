using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class LaunchFocusTests
{
    private static NoteSnapshot Note(string title) => new(Guid.NewGuid(), title, "", 0.5, false);

    private static WorkspaceSnapshot Workspace(string name, int notes, int focused)
    {
        var list = Enumerable.Range(0, notes).Select(i => Note($"{name}{i}")).ToList();
        return new WorkspaceSnapshot(Guid.NewGuid(), name, $"ws-{name}", list, list[focused].Id);
    }

    [Fact]
    public void ALaunch_StartsOnTheFirstNote_EvenIfALaterOneWasFocusedWhenClosed()
    {
        var work = Workspace("Work", 4, focused: 3);
        var snapshot = new DocumentSnapshot([work], work.Id);

        var app = SnapshotMapper.ToViewModel(snapshot, new AppConfig(), null!);

        Assert.Equal(work.Id, app.CurrentWorkspace.Id);
        Assert.Equal(0, app.CurrentWorkspace.FocusedIndex);
        Assert.True(app.CurrentWorkspace.Notes[0].IsFocused);
        Assert.False(app.CurrentWorkspace.Notes[3].IsFocused);
    }

    [Fact]
    public void ItStillOpensOnTheWorkspaceThatWasCurrent()
    {
        var a = Workspace("A", 2, 1);
        var b = Workspace("B", 3, 2);
        var snapshot = new DocumentSnapshot([a, b], b.Id);

        var app = SnapshotMapper.ToViewModel(snapshot, new AppConfig(), null!);

        Assert.Equal("B", app.CurrentWorkspace.Name);
        Assert.Equal(0, app.CurrentWorkspace.FocusedIndex);
    }

    [Fact]
    public void AnEmptyCurrentWorkspace_IsFine()
    {
        var named = new WorkspaceSnapshot(Guid.NewGuid(), "Ideas", "ws-ideas", [], null);
        var snapshot = new DocumentSnapshot([named], named.Id);

        var app = SnapshotMapper.ToViewModel(snapshot, new AppConfig(), null!);

        Assert.True(app.CurrentWorkspace.IsEmpty);
    }

    [Fact]
    public void ANewReam_StartsOnTheFirstTutorialNote()
    {
        var config = new AppConfig();
        var app = SnapshotMapper.ToViewModel(TutorialReam.Create(config, null, null), config, null!);

        Assert.Equal(0, app.CurrentWorkspace.FocusedIndex);
        Assert.True(app.CurrentWorkspace.Notes[0].IsFocused);
    }

    [Fact]
    public void ARoundTrip_ThroughASave_StillStartsAtTheFirstNote()
    {
        var work = Workspace("Work", 3, focused: 2);
        var first = SnapshotMapper.ToViewModel(new DocumentSnapshot([work], work.Id), new AppConfig(), null!);
        first.CurrentWorkspace.SetFocus(2);

        var saved = SnapshotMapper.ToSnapshot(first);
        Assert.Equal(first.CurrentWorkspace.Notes[2].Id, saved.Workspaces[0].FocusedNoteId);

        var reopened = SnapshotMapper.ToViewModel(saved, new AppConfig(), null!);
        Assert.Equal(0, reopened.CurrentWorkspace.FocusedIndex);
    }
}

public class MoveNoteBetweenWorkspacesTests
{
    private static WorkspaceViewModel Workspace(string name, int notes)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => new NoteViewModel { Title = $"{name}{i}" }), null);
        return workspace;
    }

    private static string[] Titles(WorkspaceViewModel w) => w.Notes.Select(n => n.Title).ToArray();

    [Fact]
    public void MovingDown_PutsTheNoteFirst_AndTheRestFollow()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 3);
        b.SetFocus(2);
        var app = new AppViewModel(new AppConfig(), [a, b]);
        a.SetFocus(1);

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Equal(["A1", "B0", "B1", "B2"], Titles(b));
        Assert.Equal(0, b.FocusedIndex);
        Assert.Equal("A1", b.FocusedNote!.Title);
        Assert.True(b.Notes[0].IsFocused);
        Assert.Same(b, app.CurrentWorkspace);
    }

    [Fact]
    public void MovingUp_PutsTheNoteFirstToo()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 3);
        var app = new AppViewModel(new AppConfig(), [a, b]);
        app.SwitchWorkspace(1);
        b.SetFocus(2);

        app.MoveNoteToPrevWorkspaceCommand.Execute(null);

        Assert.Equal(["B2", "A0", "A1", "A2"], Titles(a));
        Assert.Equal(0, a.FocusedIndex);
        Assert.Equal(["B0", "B1"], Titles(b));
        Assert.Same(a, app.CurrentWorkspace);
    }

    [Fact]
    public void ItDoesNotMatterWhereTheDestinationWasFocused()
    {
        foreach (int focused in new[] { 0, 1, 2 })
        {
            var a = Workspace("A", 2);
            var b = Workspace("B", 3);
            b.SetFocus(focused);
            var app = new AppViewModel(new AppConfig(), [a, b]);

            app.MoveNoteToNextWorkspaceCommand.Execute(null);

            Assert.Equal(["A0", "B0", "B1", "B2"], Titles(b));
            Assert.Equal(0, b.FocusedIndex);
        }
    }

    [Fact]
    public void SeveralMoves_StackUpInFrontOfEachOther()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 1);
        var app = new AppViewModel(new AppConfig(), [a, b]);

        app.MoveNoteToNextWorkspaceCommand.Execute(null);
        app.SwitchWorkspace(-1);
        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Equal(["A1", "A0", "B0"], Titles(b));
    }

    [Fact]
    public void IntoAnEmptyWorkspace_ItIsSimplyTheOnlyNote()
    {
        var a = Workspace("A", 2);
        var app = new AppViewModel(new AppConfig(), [a]);

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Equal(["A0"], Titles(app.CurrentWorkspace));
        Assert.Equal(0, app.CurrentWorkspace.FocusedIndex);
        Assert.Equal(["A1"], Titles(a));
    }

    [Fact]
    public void TheWorkspaceItLeft_KeepsAValidFocus()
    {
        var a = Workspace("A", 3);
        var b = Workspace("B", 1);
        var app = new AppViewModel(new AppConfig(), [a, b]);
        a.SetFocus(2);

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Equal(["A0", "A1"], Titles(a));
        Assert.Equal(1, a.FocusedIndex);
        Assert.Same(a.Notes[1], a.FocusedNote);
    }

    [Fact]
    public void AFullscreenNoteComesOutOfFullscreenWhenItMoves()
    {
        var a = Workspace("A", 2);
        var b = Workspace("B", 2);
        var app = new AppViewModel(new AppConfig(), [a, b]);
        a.FocusedNote!.IsFullscreen = true;

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.All(b.Notes, n => Assert.False(n.IsFullscreen));
    }

    [Fact]
    public void ADraftMovedAlong_IsStillTheFirstNoteThere()
    {
        var a = Workspace("A", 1);
        var b = Workspace("B", 2);
        var app = new AppViewModel(new AppConfig(), [a, b]);
        app.NewNoteCommand.Execute(null);
        var draft = a.FocusedNote!;

        app.MoveNoteToNextWorkspaceCommand.Execute(null);

        Assert.Same(draft, b.Notes[0]);
        Assert.True(draft.IsDraft);
    }

    [Fact]
    public void TheRowArrivesInPlace_WithoutSlidingSideways() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("A", 2), ("B", 6));
        var b = fx.App.Workspaces[2];
        b.SetFocus(5);
        Ui.Settle();
        Ui.Settle();

        fx.App.MoveNoteToNextWorkspaceCommand.Execute(null);
        Ui.Settle();

        var first = fx.ColumnOf(b.Notes[0]);
        var row = Ui.Ancestor<Ream.App.Controls.NoteRowPanel>(first)!;
        double centered = (row.ActualWidth - first.ActualWidth) / 2;
        Assert.Equal(centered, first.TranslatePoint(new System.Windows.Point(0, 0), row).X, 1);
    });
}
