using Ream.Core.Abstractions;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

/// <summary>Translates between the persisted snapshot and the live view models.</summary>
internal static class SnapshotMapper
{
    public static AppViewModel ToViewModel(DocumentSnapshot snapshot, AppConfig config, IAssetStore assets)
    {
        var (workspaces, current) = ToWorkspaces(snapshot, assets);
        var app = new AppViewModel(config, workspaces, current, assets);

        // A launch starts at the beginning of the row, whichever note had focus when Ream was closed.
        app.CurrentWorkspace.SetFocus(0);
        return app;
    }

    /// <summary>Builds the workspaces of a snapshot, and which one to show (an index into them).</summary>
    public static (List<WorkspaceViewModel> Workspaces, int CurrentIndex) ToWorkspaces(DocumentSnapshot snapshot, IAssetStore assets)
    {
        var workspaces = snapshot.Workspaces.Select(w => ToWorkspace(w, assets)).ToList();
        int current = workspaces.FindIndex(w => w.Id == snapshot.CurrentWorkspaceId);
        return (workspaces, Math.Max(0, current));
    }

    /// <summary>Puts a loaded ream into an existing view model, at the beginning of its first row like a launch.</summary>
    public static void LoadInto(AppViewModel app, DocumentSnapshot snapshot, IAssetStore assets)
    {
        var (workspaces, current) = ToWorkspaces(snapshot, assets);
        app.LoadReam(workspaces, current, assets);
        app.CurrentWorkspace.SetFocus(0);
    }

    public static DocumentSnapshot ToSnapshot(AppViewModel app)
    {
        // Edits live in the editors' documents until now; bring the saved form up to date first.
        app.FlushPendingContent();

        // The empty workspaces at either edge are recreated on load, so a workspace is stored only when
        // the user named it or it holds something worth keeping (a blank draft isn't).
        var persisted = app.Workspaces.Where(w => w.Name is not null || SavedNotes(w).Count > 0).ToList();

        var current = app.CurrentWorkspace;
        Guid? currentId = persisted.Contains(current) ? current.Id : null;

        return new DocumentSnapshot(persisted.Select(ToSnapshot).ToList(), currentId);
    }

    private static List<NoteViewModel> SavedNotes(WorkspaceViewModel workspace) =>
        workspace.Notes.Where(n => !n.IsBlankDraft()).ToList();

    private static WorkspaceSnapshot ToSnapshot(WorkspaceViewModel workspace)
    {
        var notes = SavedNotes(workspace);
        var focused = workspace.FocusedNote;
        return new WorkspaceSnapshot(
            workspace.Id,
            workspace.Name,
            workspace.FolderName,
            notes.Select(n => new NoteSnapshot(n.Id, n.Title, n.Body, n.WidthFraction, n.IsFullscreen, n.CustomTitle)).ToList(),
            focused is not null && notes.Contains(focused) ? focused.Id : null);
    }

    private static WorkspaceViewModel ToWorkspace(WorkspaceSnapshot snapshot, IAssetStore assets)
    {
        var workspace = new WorkspaceViewModel(snapshot.Id, snapshot.Name, snapshot.FolderName, assets);
        workspace.LoadNotes(
            snapshot.Notes.Select(n => new NoteViewModel
            {
                Id = n.Id,
                Title = n.Title,
                CustomTitle = n.CustomTitle,
                Body = n.Body,
                WidthFraction = n.WidthFraction,
                IsFullscreen = n.IsFullscreen,
            }),
            snapshot.FocusedNoteId);
        return workspace;
    }
}
