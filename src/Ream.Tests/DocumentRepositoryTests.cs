using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class DocumentRepositoryTests
{
    private static NoteSnapshot Note(string title, string body = "", WidthPreset width = WidthPreset.Half, bool fullscreen = false) =>
        new(Guid.NewGuid(), title, body, width, fullscreen);

    private static WorkspaceSnapshot Workspace(string? name, string folder, params NoteSnapshot[] notes) =>
        new(Guid.NewGuid(), name, folder, notes, notes.Length > 0 ? notes[^1].Id : null);

    private static DocumentSnapshot Doc(Guid? current, params WorkspaceSnapshot[] workspaces) => new(workspaces, current);

    [Fact]
    public void Load_EmptyRoot_IsFirstRunWithNoWorkspaces()
    {
        using var dir = new TempDir();
        var snapshot = new DocumentRepository(dir.Combine("ReemDocuments")).Load();

        Assert.True(snapshot.IsFirstRun);
        Assert.Empty(snapshot.Workspaces);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");

        var a = Note("Groceries", "milk\neggs", WidthPreset.OneThird);
        var b = Note("Ideas", "big ideas", WidthPreset.Full, fullscreen: true);
        var personal = Workspace("Personal", "ws-aaaaaaaa", a, b) with { FocusedNoteId = a.Id };
        var unnamed = Workspace(null, "ws-bbbbbbbb", Note("Loose"));

        new DocumentRepository(root).Save(Doc(unnamed.Id, personal, unnamed));

        var loaded = new DocumentRepository(root).Load();

        Assert.False(loaded.IsFirstRun);
        Assert.Equal(unnamed.Id, loaded.CurrentWorkspaceId);
        Assert.Equal(["Personal", null], loaded.Workspaces.Select(w => w.Name));

        var loadedPersonal = loaded.Workspaces[0];
        Assert.Equal(personal.Id, loadedPersonal.Id);
        Assert.Equal(a.Id, loadedPersonal.FocusedNoteId);
        Assert.Equal([a.Id, b.Id], loadedPersonal.Notes.Select(n => n.Id));
        Assert.Equal("milk\neggs", loadedPersonal.Notes[0].Body);
        Assert.Equal(WidthPreset.OneThird, loadedPersonal.Notes[0].Width);
        Assert.Equal(WidthPreset.Full, loadedPersonal.Notes[1].Width);
        Assert.True(loadedPersonal.Notes[1].IsFullscreen);
        Assert.Equal("Ideas", loadedPersonal.Notes[1].Title);
    }

    [Fact]
    public void Save_WritesTheDocumentedLayoutAndLeavesNoTempFiles()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("Hello", "body");

        new DocumentRepository(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));

        Assert.True(File.Exists(Path.Combine(root, "metadata.json")));
        Assert.True(File.Exists(Path.Combine(root, "ws-11111111", "layout.json")));
        Assert.Equal("body", File.ReadAllText(Path.Combine(root, "ws-11111111", note.Id.ToString("N") + ".reamnote")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Layout_StoresWidthAsReadableText()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");

        new DocumentRepository(root).Save(Doc(null, Workspace("W", "ws-11111111", Note("N", width: WidthPreset.TwoThirds))));

        string json = File.ReadAllText(Path.Combine(root, "ws-11111111", "layout.json"));
        Assert.Contains("\"width\": \"twoThirds\"", json);
    }

    [Fact]
    public void RemovedNote_IsMovedToTrashNotDeleted()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var keep = Note("Keep", "k");
        var drop = Note("Drop", "precious");
        var repo = new DocumentRepository(root);
        var ws = Workspace("W", "ws-11111111", keep, drop);
        repo.Save(Doc(null, ws));

        repo.Save(Doc(null, ws with { Notes = [keep] }));

        string file = drop.Id.ToString("N") + ".reamnote";
        Assert.False(File.Exists(Path.Combine(root, "ws-11111111", file)));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(root, ".trash", "ws-11111111", file)));
        Assert.Equal([keep.Id], new DocumentRepository(root).Load().Workspaces[0].Notes.Select(n => n.Id));
    }

    [Fact]
    public void NoteMovedBetweenWorkspaces_MovesItsFile()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var moving = Note("Mover", "content");
        var source = Workspace("From", "ws-11111111", moving);
        var target = Workspace("To", "ws-22222222", Note("Resident"));
        var repo = new DocumentRepository(root);
        repo.Save(Doc(null, source, target));

        repo.Save(Doc(null, source with { Notes = [] }, target with { Notes = [.. target.Notes, moving] }));

        string file = moving.Id.ToString("N") + ".reamnote";
        Assert.False(File.Exists(Path.Combine(root, "ws-11111111", file)));
        Assert.Equal("content", File.ReadAllText(Path.Combine(root, "ws-22222222", file)));
        Assert.False(Directory.Exists(Path.Combine(root, ".trash")));
    }

    [Fact]
    public void WorkspaceDroppedFromSnapshot_HasItsFolderRemoved()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var keep = Workspace("Keep", "ws-11111111", Note("A"));
        var gone = Workspace("Gone", "ws-22222222");
        var repo = new DocumentRepository(root);
        repo.Save(Doc(null, keep, gone));
        Assert.True(Directory.Exists(Path.Combine(root, "ws-22222222")));

        repo.Save(Doc(null, keep));

        Assert.False(Directory.Exists(Path.Combine(root, "ws-22222222")));
        Assert.Single(new DocumentRepository(root).Load().Workspaces);
    }

    [Fact]
    public void MissingLayout_IsRebuiltFromNoteFiles()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("First line title", "First line title\nmore");
        new DocumentRepository(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.Delete(Path.Combine(root, "ws-11111111", "layout.json"));

        var loaded = new DocumentRepository(root).Load();

        var recovered = Assert.Single(Assert.Single(loaded.Workspaces).Notes);
        Assert.Equal(note.Id, recovered.Id);
        Assert.Equal("First line title", recovered.Title);
    }

    [Fact]
    public void NoteFileMissingFromLayout_IsAdopted()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var known = Note("Known");
        new DocumentRepository(root).Save(Doc(null, Workspace("W", "ws-11111111", known)));

        var stray = Guid.NewGuid();
        File.WriteAllText(Path.Combine(root, "ws-11111111", stray.ToString("N") + ".reamnote"), "found me");

        var notes = new DocumentRepository(root).Load().Workspaces[0].Notes;

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Id == stray && n.Body == "found me");
    }

    [Fact]
    public void CorruptMetadata_IsSetAsideAndWorkspaceFoldersAreAdopted()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        new DocumentRepository(root).Save(Doc(null, Workspace("W", "ws-11111111", Note("Survivor"))));
        File.WriteAllText(Path.Combine(root, "metadata.json"), "{ this is not json");

        var loaded = new DocumentRepository(root).Load();

        Assert.False(File.Exists(Path.Combine(root, "metadata.json")));
        Assert.Single(Directory.GetFiles(root, "metadata.json.corrupt-*"));
        var workspace = Assert.Single(loaded.Workspaces);
        Assert.Equal("Survivor", Assert.Single(workspace.Notes).Title);
        Assert.False(loaded.IsFirstRun);
    }

    [Fact]
    public void CorruptLayout_IsSetAsideAndNotesAreStillLoaded()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("Kept", "text");
        new DocumentRepository(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.WriteAllText(Path.Combine(root, "ws-11111111", "layout.json"), "garbage");

        var loaded = new DocumentRepository(root).Load();

        Assert.Equal(note.Id, Assert.Single(loaded.Workspaces[0].Notes).Id);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "ws-11111111"), "layout.json.corrupt-*"));
    }

    [Fact]
    public void MetadataWithPathTraversal_IsIgnored()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(dir.Combine("outside"));
        File.WriteAllText(
            Path.Combine(root, "metadata.json"),
            """{ "schemaVersion": 1, "workspaces": [ { "id": "6c1f4d2e-0000-4000-8000-000000000001", "folderName": "..\\outside", "order": 0 } ] }""");

        var loaded = new DocumentRepository(root).Load();

        Assert.Empty(loaded.Workspaces);
    }

    [Fact]
    public void FileFromNewerVersion_IsRefusedRatherThanOverwritten()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "metadata.json"), """{ "schemaVersion": 99, "workspaces": [] }""");

        Assert.Throws<InvalidDataException>(() => new DocumentRepository(root).Load());
        Assert.True(File.Exists(Path.Combine(root, "metadata.json")));
    }

    [Fact]
    public void EditedBody_IsWrittenOnNextSave()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("N", "v1");
        var repo = new DocumentRepository(root);
        var ws = Workspace("W", "ws-11111111", note);
        repo.Save(Doc(null, ws));

        repo.Save(Doc(null, ws with { Notes = [note with { Body = "v2" }] }));

        Assert.Equal("v2", new DocumentRepository(root).Load().Workspaces[0].Notes[0].Body);
    }

    [Fact]
    public void DeriveTitle_UsesFirstNonEmptyLineAndCapsLength()
    {
        Assert.Equal("Hello", DocumentRepository.DeriveTitle("\n  \n Hello \nworld"));
        Assert.Equal("Untitled", DocumentRepository.DeriveTitle("  \n \n"));
        Assert.Equal(60, DocumentRepository.DeriveTitle(new string('x', 200)).Length);
    }
}
