using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class WorkspaceTitleTests
{
    private static WorkspaceViewModel Workspace(string? name, int notes = 1)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(Enumerable.Range(0, notes).Select(i => new NoteViewModel { Title = $"n{i}" }), null);
        return workspace;
    }

    private static AppViewModel App(params WorkspaceViewModel[] workspaces) => new(new AppConfig(), workspaces);

    [Fact]
    public void ANamedWorkspace_IsTheTitleAfterTheAppName()
    {
        var app = App(Workspace("Work"));

        Assert.Equal("Ream - Work", app.WindowTitle);
    }

    [Fact]
    public void AnUnnamedOne_IsShownByItsNumber()
    {
        var app = App(Workspace("A"), Workspace(null));
        app.SwitchWorkspace(1);

        Assert.Equal("Ream - Workspace 2", app.WindowTitle);
    }

    [Fact]
    public void TheEmptyEdges_HaveNothingToShow_SoTheTitleIsJustTheAppName()
    {
        var app = App(Workspace("Work"));

        app.SwitchWorkspaceUpCommand.Execute(null);
        Assert.Equal("Ream", app.WindowTitle);

        app.SwitchWorkspace(2);
        Assert.Equal("Ream", app.WindowTitle);
    }

    [Fact]
    public void TheTitleFollowsSwitches_AndRenames()
    {
        var app = App(Workspace("A"), Workspace("B"));
        var titles = new List<string>();
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.WindowTitle)) titles.Add(app.WindowTitle);
        };

        app.SwitchWorkspace(1);
        app.CurrentWorkspace.Name = "Renamed";

        Assert.Equal("Ream - Renamed", app.WindowTitle);
        Assert.Contains("Ream - B", titles);
        Assert.Contains("Ream - Renamed", titles);
    }

    [Fact]
    public void RenamingAWorkspaceThatIsNotCurrent_LeavesTheTitleAlone()
    {
        var app = App(Workspace("A"), Workspace("B"));
        int changes = 0;
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.WindowTitle)) changes++;
        };

        app.Workspaces[2].Name = "Elsewhere";

        Assert.Equal(0, changes);
        Assert.Equal("Ream - A", app.WindowTitle);
    }

    [Fact]
    public void ANumberedTitle_UpdatesWhenAnEmptyWorkspaceAboveItIsPrunedAway()
    {
        var app = App(Workspace("A"), Workspace(null, 0), Workspace(null));
        app.SelectWorkspaceCommand.Execute(app.Workspaces[3]);
        Assert.Equal("Ream - Workspace 3", app.WindowTitle);

        app.PruneEmptyWorkspacesCommand.Execute(null);

        Assert.Equal("Ream - Workspace 2", app.WindowTitle);
    }
    [Fact]
    public void TheTitleIsSafeWhenTheIndexIsTemporarilyOutOfRange()
    {
        var app = App(Workspace("A"));
        app.CurrentIndex = 2;

        app.Workspaces.RemoveAt(2);

        Assert.Equal("Ream", app.WindowTitle);
    }

    [Fact]
    public void AClearedName_FallsBackToTheNumber()
    {
        var app = App(Workspace("Work"));

        app.CurrentWorkspace.Name = null;

        Assert.Equal("Ream - Workspace 1", app.WindowTitle);
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
