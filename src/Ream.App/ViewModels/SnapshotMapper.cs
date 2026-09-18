using Ream.Core.Abstractions;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

/// <summary>Translates between the persisted snapshot and the live view models.</summary>
internal static class SnapshotMapper
{
    public static AppViewModel ToViewModel(DocumentSnapshot snapshot, AppConfig config, IAssetStore assets)
    {
        var workspaces = snapshot.Workspaces.Select(w => ToWorkspace(w, assets)).ToList();
        int current = workspaces.FindIndex(w => w.Id == snapshot.CurrentWorkspaceId);
        return new AppViewModel(config, workspaces, Math.Max(0, current), assets);
    }

    public static DocumentSnapshot ToSnapshot(AppViewModel app)
    {
        // Edits live in the editors' documents until now; bring the saved form up to date first.
        app.FlushPendingContent();

        // The trailing empty workspace is always recreated on load, so it is never stored.
        var persisted = app.Workspaces.ToList();
        if (persisted.Count > 0 && persisted[^1].IsEmpty)
            persisted.RemoveAt(persisted.Count - 1);

        var current = app.CurrentWorkspace;
        Guid? currentId = persisted.Contains(current) ? current.Id : null;

        return new DocumentSnapshot(persisted.Select(ToSnapshot).ToList(), currentId);
    }

    private static WorkspaceSnapshot ToSnapshot(WorkspaceViewModel workspace) => new(
        workspace.Id,
        workspace.Name,
        workspace.FolderName,
        workspace.Notes.Select(n => new NoteSnapshot(n.Id, n.Title, n.Body, n.WidthPreset, n.IsFullscreen)).ToList(),
        workspace.FocusedNote?.Id);

    private static WorkspaceViewModel ToWorkspace(WorkspaceSnapshot snapshot, IAssetStore assets)
    {
        var workspace = new WorkspaceViewModel(snapshot.Id, snapshot.Name, snapshot.FolderName, assets);
        workspace.LoadNotes(
            snapshot.Notes.Select(n => new NoteViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Body = n.Body,
                WidthPreset = n.Width,
                IsFullscreen = n.IsFullscreen,
                AccentColor = AccentFor(n.Id),
            }),
            snapshot.FocusedNoteId);
        return workspace;
    }

    private static readonly string[] Accents = ["#f2a65a", "#7c9cff", "#8bd3a8", "#e57a9a", "#b48cf2"];

    /// <summary>Accent colors aren't persisted; deriving one from the id keeps each note's color stable.</summary>
    public static string AccentFor(Guid id) => Accents[id.ToByteArray()[0] % Accents.Length];
}
