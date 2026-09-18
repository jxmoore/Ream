using Ream.Core.Models;

namespace Ream.Core.Abstractions;

public interface IDocumentRepository
{
    /// <summary>Reads everything from storage. Never returns partial or corrupt data; damaged files are set aside.</summary>
    DocumentSnapshot Load();

    /// <summary>Makes storage match the snapshot: creates, moves, and removes notes and workspaces as needed.</summary>
    void Save(DocumentSnapshot snapshot);
}
