using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class WorkspaceLabelTests
{
    private static WorkspaceViewModel Workspace(string? name, int notes = 1)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => new NoteViewModel { Title = $"n{i}" }), null);
        return workspace;
    }

    private static AppViewModel App(params WorkspaceViewModel[] workspaces) => new(new AppConfig(), workspaces);

    [Fact]
    public void ANamedWorkspace_IsItsOwnLabel()
    {
        var app = App(Workspace("Work"));

        Assert.Equal("Work", app.WorkspaceLabel);
    }

    [Fact]
    public void AnUnnamedOne_IsShownByItsNumber()
    {
        var app = App(Workspace("A"), Workspace(null));
        app.SwitchWorkspace(1);

        Assert.Equal("Workspace 2", app.WorkspaceLabel);
    }

    [Fact]
    public void TheEmptyEdges_AreLabelledAsNew()
    {
        var app = App(Workspace("Work"));

        app.CurrentIndex = 0;
        Assert.Equal("New workspace", app.WorkspaceLabel);

        app.CurrentIndex = 2;
        Assert.Equal("New workspace", app.WorkspaceLabel);
    }

    [Fact]
    public void TheLabelFollowsSwitches_AndRenames()
    {
        var app = App(Workspace("A"), Workspace("B"));
        var titles = new List<string>();
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.WorkspaceLabel)) titles.Add(app.WorkspaceLabel);
        };

        app.SwitchWorkspace(1);
        app.CurrentWorkspace.Name = "Renamed";

        Assert.Equal("Renamed", app.WorkspaceLabel);
        Assert.Contains("B", titles);
        Assert.Contains("Renamed", titles);
    }

    [Fact]
    public void RenamingAWorkspaceThatIsNotCurrent_LeavesTheLabelAlone()
    {
        var app = App(Workspace("A"), Workspace("B"));
        int changes = 0;
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.WorkspaceLabel)) changes++;
        };

        app.Workspaces[2].Name = "Elsewhere";

        Assert.Equal(0, changes);
        Assert.Equal("A", app.WorkspaceLabel);
    }

    [Fact]
    public void ANumberedLabel_UpdatesWhenAnEmptyWorkspaceAboveItIsPrunedAway()
    {
        var app = App(Workspace("A"), Workspace(null, 0), Workspace(null));
        app.SelectWorkspaceCommand.Execute(app.Workspaces[3]);
        Assert.Equal("Workspace 3", app.WorkspaceLabel);

        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Equal("Workspace 2", app.WorkspaceLabel);
    }

    [Fact]
    public void TheLabelIsSafeWhenTheIndexIsTemporarilyOutOfRange()
    {
        var app = App(Workspace("A"));
        app.CurrentIndex = 2;

        app.Workspaces.RemoveAt(2);

        Assert.Equal("New workspace", app.WorkspaceLabel);
    }

    [Fact]
    public void AClearedName_FallsBackToTheNumber()
    {
        var app = App(Workspace("Work"));

        app.CurrentWorkspace.Name = null;

        Assert.Equal("Workspace 1", app.WorkspaceLabel);
    }

    [Theory]
    [InlineData("Home", false, 3, "Home", "Home")]
    [InlineData(null, false, 3, "Workspace 3", "Workspace 3")]
    [InlineData(null, true, 0, null, "New workspace above")]
    [InlineData(null, true, 4, null, "New workspace below")]
    [InlineData("Inbox", true, 0, "Inbox", "Inbox")]
    public void DisplayNameAndMenuLabel(string? name, bool edge, int number, string? display, string menu)
    {
        var workspace = new WorkspaceViewModel(name) { IsEdge = edge, Number = number };

        Assert.Equal(display, workspace.DisplayName);
        Assert.Equal(menu, workspace.MenuLabel);
    }
}

public class DefaultBrushTests
{
    [Fact]
    public void EveryBrushTheWindowUses_ExistsBeforeAnyThemeIsApplied() => Ui.Run(() =>
    {
        foreach (var key in new[] { "CanvasBrush", "RibbonBrush", "FocusBorderBrush" })
            Assert.True(System.Windows.Application.Current.Resources.Contains(key), $"{key} has no default in App.xaml");
    });
}
