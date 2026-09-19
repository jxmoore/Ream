using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class WorkspaceViewModelTests
{
    private static NoteViewModel Note(string title = "n") => new() { Title = title };

    private static WorkspaceViewModel Workspace(string? name, int notes)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => Note($"note {i}")), null);
        return workspace;
    }

    private static AppViewModel App(params WorkspaceViewModel[] workspaces) => new(new AppConfig(), workspaces);

    // ----- Numbering, selection, labels -----

    [Fact]
    public void Workspaces_AreNumberedAndTheCurrentOneIsFlagged()
    {
        var app = App(Workspace("Work", 1), Workspace(null, 1));

        Assert.Equal(["+", "Work", "2", "+"], app.Workspaces.Select(w => w.DisplayLabel));
        Assert.Equal([false, true, false, false], app.Workspaces.Select(w => w.IsCurrent));
    }

    [Fact]
    public void SelectingAWorkspace_SwitchesToItAndMovesTheFlag()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));

        app.SelectWorkspaceCommand.Execute(app.Workspaces[2]);

        Assert.Equal(2, app.CurrentIndex);
        Assert.Equal([false, false, true, false], app.Workspaces.Select(w => w.IsCurrent));
    }

    [Fact]
    public void SelectingAWorkspaceSeveralStepsAway_JumpsStraightThere()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1), Workspace("C", 1));

        app.SelectWorkspaceCommand.Execute(app.Workspaces[3]);

        Assert.Equal(3, app.CurrentIndex);
    }

    [Fact]
    public void SelectingNothingOrAStranger_ChangesNothing()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));

        app.SelectWorkspaceCommand.Execute(null);
        app.SelectWorkspaceCommand.Execute(new WorkspaceViewModel("Stranger"));

        Assert.Equal(1, app.CurrentIndex);
    }

    [Fact]
    public void NumbersFollowTheList_WhenWorkspacesAreAddedOrRemoved()
    {
        var app = App(Workspace(null, 1), Workspace(null, 1));
        Assert.Equal(["+", "1", "2", "+"], app.Workspaces.Select(w => w.DisplayLabel));

        // A note in the trailing workspace makes a new trailing one appear.
        app.CurrentIndex = 3;
        app.NewNoteCommand.Execute(null);
        Assert.Equal(["+", "1", "2", "3", "+"], app.Workspaces.Select(w => w.DisplayLabel));

        app.Workspaces.RemoveAt(1);
        Assert.Equal(["+", "1", "2", "+"], app.Workspaces.Select(w => w.DisplayLabel));
    }

    // ----- Renaming -----

    [Fact]
    public void Rename_TrimsAndAppliesTheName()
    {
        var workspace = Workspace(null, 1);

        workspace.BeginRename();
        workspace.EditName = "  Projects  ";
        workspace.CommitRename();

        Assert.Equal("Projects", workspace.Name);
        Assert.False(workspace.IsRenaming);
    }

    [Fact]
    public void BeginningARename_StartsFromTheCurrentName()
    {
        var workspace = Workspace("Old", 1);

        workspace.BeginRename();

        Assert.True(workspace.IsRenaming);
        Assert.Equal("Old", workspace.EditName);
    }

    [Fact]
    public void ABlankName_ClearsTheNameSoTheNumberShowsAgain()
    {
        var app = App(Workspace("Named", 1));

        var workspace = app.Workspaces[1];
        workspace.BeginRename();
        workspace.EditName = "   ";
        workspace.CommitRename();

        Assert.Null(workspace.Name);
        Assert.Equal("1", workspace.DisplayLabel);
    }

    [Fact]
    public void CancellingARename_KeepsTheOldName()
    {
        var workspace = Workspace("Keep", 1);

        workspace.BeginRename();
        workspace.EditName = "Changed";
        workspace.CancelRename();
        workspace.CommitRename();

        Assert.Equal("Keep", workspace.Name);
    }

    [Fact]
    public void CommitWithoutARename_DoesNothing()
    {
        var workspace = Workspace("Keep", 1);
        workspace.EditName = "Sneaky";

        workspace.CommitRename();

        Assert.Equal("Keep", workspace.Name);
    }

    [Fact]
    public void VeryLongNames_AreCapped()
    {
        var workspace = Workspace(null, 1);

        workspace.BeginRename();
        workspace.EditName = new string('x', 200);
        workspace.CommitRename();

        Assert.Equal(40, workspace.Name!.Length);
    }

    [Fact]
    public void RenameCommands_AreHandledByTheApp()
    {
        var app = App(Workspace("A", 1), Workspace("B", 1));
        app.CurrentIndex = 2;

        app.BeginRenameCommand.Execute(null);

        Assert.True(app.Workspaces[2].IsRenaming);
        Assert.False(app.Workspaces[1].IsRenaming);

        app.Workspaces[2].EditName = "Renamed";
        app.CommitRenameCommand.Execute(app.Workspaces[2]);
        Assert.Equal("Renamed", app.Workspaces[2].Name);

        app.BeginRenameCommand.Execute(app.Workspaces[1]);
        app.CancelRenameCommand.Execute(app.Workspaces[1]);
        Assert.False(app.Workspaces[1].IsRenaming);
    }

    [Fact]
    public void RenameIsBoundToItsConfiguredShortcut()
    {
        var app = App(Workspace("A", 1));

        Assert.Same(app.BeginRenameCommand, app.Actions["renameWorkspace"]);
        Assert.Equal("Shift+F2", app.Config.Keybindings["renameWorkspace"]);
    }

    [Fact]
    public void RenameRequestsEditorFocusWhenFinished()
    {
        var app = App(Workspace("A", 1));
        int requests = 0;
        app.FocusEditorRequested += () => requests++;

        app.Workspaces[0].BeginRename();
        app.CommitRenameCommand.Execute(app.Workspaces[0]);
        app.CancelRenameCommand.Execute(app.Workspaces[0]);

        Assert.Equal(2, requests);
    }

    // ----- Named workspaces survive -----

    [Fact]
    public void EmptyUnnamedWorkspaces_ArePrunedButNamedOnesStay()
    {
        var named = Workspace("Keep me", 0);
        var unnamed = Workspace(null, 0);
        var app = App(named, unnamed, Workspace(null, 1));
        app.CurrentIndex = 2;

        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Contains(named, app.Workspaces);
        Assert.DoesNotContain(unnamed, app.Workspaces);
        Assert.Equal(app.Workspaces[^2], app.CurrentWorkspace);
    }

    [Fact]
    public void PruningKeepsNumbersAndTheCurrentFlagConsistent()
    {
        var app = App(Workspace(null, 1), Workspace(null, 0), Workspace(null, 1));
        app.CurrentIndex = 3;

        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Equal(["+", "1", "2", "+"], app.Workspaces.Select(w => w.DisplayLabel));
        Assert.Equal(2, app.CurrentIndex);
        Assert.Equal([false, false, true, false], app.Workspaces.Select(w => w.IsCurrent));
    }

    // ----- What gets saved -----

    [Fact]
    public void NamedTrailingWorkspace_IsSaved_ButAnUnnamedOneIsNot()
    {
        var app = App(Workspace("First", 1));
        Assert.Single(SnapshotMapper.ToSnapshot(app).Workspaces);

        app.Workspaces[^1].Name = "Ideas";

        var saved = SnapshotMapper.ToSnapshot(app).Workspaces;
        Assert.Equal(["First", "Ideas"], saved.Select(w => w.Name));
    }

    [Fact]
    public void WorkspaceNames_AreSaved()
    {
        var app = App(Workspace("Before", 1));
        app.Workspaces[1].Name = "After";

        Assert.Equal("After", SnapshotMapper.ToSnapshot(app).Workspaces.Single().Name);
    }

    // ----- Widths -----

    [Fact]
    public void CyclingWidth_StepsThroughPresets_FromAnyDraggedWidth()
    {
        var app = App(Workspace("A", 1));
        var note = app.CurrentWorkspace.Notes[0];

        note.WidthFraction = 0.4;
        app.CycleWidthPresetCommand.Execute(null);
        Assert.Equal(0.5, note.WidthFraction, 9);

        note.WidthFraction = 0.9;
        app.CycleWidthPresetCommand.Execute(null);
        Assert.Equal(1.0, note.WidthFraction, 9);

        app.CycleWidthPresetCommand.Execute(null);
        Assert.Equal(1d / 3d, note.WidthFraction, 9);
    }


    [Fact]
    public void DraggedWidth_IsSavedInTheSnapshot()
    {
        var app = App(Workspace("A", 1));
        app.CurrentWorkspace.Notes[0].WidthFraction = 0.42;

        Assert.Equal(0.42, SnapshotMapper.ToSnapshot(app).Workspaces[0].Notes[0].WidthFraction, 9);
    }

    [Fact]
    public void FocusNoteBy_MovesFocusAndAsksForTheEditor()
    {
        var app = App(Workspace("A", 3));
        int requests = 0;
        app.FocusEditorRequested += () => requests++;

        app.FocusNoteBy(1);
        app.FocusNoteBy(1);
        app.FocusNoteBy(0);
        app.FocusNoteBy(-1);

        Assert.Equal(1, app.CurrentWorkspace.FocusedIndex);
        Assert.Equal(3, requests);
    }
}
