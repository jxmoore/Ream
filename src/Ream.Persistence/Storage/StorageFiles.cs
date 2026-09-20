using Ream.Core.Models;

namespace Ream.Persistence.Storage;

internal interface IVersionedFile
{
    int SchemaVersion { get; }
}

/// <summary>Foo.ream: the workspace list, and where the ream's data folder is (relative to this file; "." = the same folder).</summary>
internal sealed class ReamFile : IVersionedFile
{
    public int SchemaVersion { get; set; } = StorageConstants.SchemaVersion;
    public string? DataFolder { get; set; }
    public List<WorkspaceEntryFile>? Workspaces { get; set; } = [];
    public Guid? CurrentWorkspaceId { get; set; }
}

internal sealed class WorkspaceEntryFile
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? FolderName { get; set; }
    public int Order { get; set; }
}

/// <summary>ws-xxxxxxxx/layout.reamlayout</summary>
internal sealed class LayoutFile : IVersionedFile
{
    public int SchemaVersion { get; set; } = StorageConstants.SchemaVersion;
    public List<NoteEntryFile>? Notes { get; set; } = [];
    public Guid? FocusedNoteId { get; set; }
}

internal sealed class NoteEntryFile
{
    public Guid NoteId { get; set; }
    public string? FileName { get; set; }
    public string? Title { get; set; }

    /// <summary>A title the user typed; when absent the note shows <see cref="Title"/>, taken from its first line.</summary>
    public string? CustomTitle { get; set; }

    /// <summary>Fraction of the row's width. Files written before freeform resizing have <see cref="Width"/> instead.</summary>
    public double? WidthFraction { get; set; }

    /// <summary>Legacy named width; read for older files, never written.</summary>
    public WidthPreset? Width { get; set; }

    public bool IsFullscreen { get; set; }
}

internal static class StorageConstants
{
    public const int SchemaVersion = 1;
    public const string LayoutFileName = "layout" + ReamPaths.LayoutExtension;
    public const string NoteExtension = ReamPaths.NoteExtension;

    // What a ream was called before it had a file of its own: <folder>/metadata.json and <ws>/layout.json.
    public const string LegacyMetadataFileName = "metadata.json";
    public const string LegacyLayoutFileName = "layout.json";
    public const string WorkspaceFolderPrefix = "ws-";
    public const string TrashFolderName = ".trash";
    public const string RecoveredFolderName = ".recovered";
}
