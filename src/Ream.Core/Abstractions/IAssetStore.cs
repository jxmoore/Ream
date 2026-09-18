namespace Ream.Core.Abstractions;

/// <summary>Stores the images that belong to a note, next to the note on disk.</summary>
public interface IAssetStore
{
    /// <summary>Saves PNG bytes for a note and returns the asset's file name.</summary>
    string SaveAsset(string workspaceFolder, Guid noteId, byte[] png);

    /// <summary>Full path of a previously saved asset, or null if it is missing or the name is unsafe.</summary>
    string? GetAssetPath(string workspaceFolder, Guid noteId, string name);
}
