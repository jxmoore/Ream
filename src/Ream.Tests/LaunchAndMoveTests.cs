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
    public void TheFirstEverLaunch_StartsOnTheWelcomeNote()
    {
        var app = SeedData.CreateWelcome(new AppConfig(), null!);

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
