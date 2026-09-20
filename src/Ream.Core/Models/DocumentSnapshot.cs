namespace Ream.Core.Models;

/// <summary>A point-in-time, storage-agnostic view of everything that is persisted.</summary>
public sealed record DocumentSnapshot(
    IReadOnlyList<WorkspaceSnapshot> Workspaces,
    Guid? CurrentWorkspaceId,
    bool IsFirstRun = false);

public sealed record WorkspaceSnapshot(
    Guid Id,
    string? Name,
    string FolderName,
    IReadOnlyList<NoteSnapshot> Notes,
    Guid? FocusedNoteId);

public sealed record NoteSnapshot(
    Guid Id,
    string Title,
    string Body,
    double WidthFraction,
    bool IsFullscreen,
    string? CustomTitle = null);
