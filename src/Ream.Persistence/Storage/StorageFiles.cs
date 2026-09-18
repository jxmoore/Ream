using Ream.Core.Models;

namespace Ream.Persistence.Storage;

internal interface IVersionedFile
{
    int SchemaVersion { get; }
}

/// <summary>ReemDocuments/metadata.json</summary>
internal sealed class MetadataFile : IVersionedFile
{
    public int SchemaVersion { get; set; } = StorageConstants.SchemaVersion;
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

/// <summary>ReemDocuments/ws-xxxxxxxx/layout.json</summary>
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
    public WidthPreset Width { get; set; } = WidthPreset.Half;
    public bool IsFullscreen { get; set; }
}

internal static class StorageConstants
{
    public const int SchemaVersion = 1;
    public const string MetadataFileName = "metadata.json";
    public const string LayoutFileName = "layout.json";
    public const string NoteExtension = ".reamnote";
    public const string WorkspaceFolderPrefix = "ws-";
    public const string TrashFolderName = ".trash";
}
