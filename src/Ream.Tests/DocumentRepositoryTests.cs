using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class DocumentRepositoryTests
{
    private static NoteSnapshot Note(string title, string body = "", double width = 0.5, bool fullscreen = false) =>
        new(Guid.NewGuid(), title, body, width, fullscreen);

    private static WorkspaceSnapshot Workspace(string? name, string folder, params NoteSnapshot[] notes) =>
        new(Guid.NewGuid(), name, folder, notes, notes.Length > 0 ? notes[^1].Id : null);

    private static DocumentSnapshot Doc(Guid? current, params WorkspaceSnapshot[] workspaces) => new(workspaces, current);

    [Fact]
    public void Load_EmptyRoot_IsFirstRunWithNoWorkspaces()
    {
        using var dir = new TempDir();
        var snapshot = TestReam.Repo(dir.Combine("ReemDocuments")).Load();

        Assert.True(snapshot.IsFirstRun);
        Assert.Empty(snapshot.Workspaces);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");

        var a = Note("Groceries", "milk\neggs", 1d / 3d);
        var b = Note("Ideas", "big ideas", 1.0, fullscreen: true);
        var personal = Workspace("Personal", "ws-aaaaaaaa", a, b) with { FocusedNoteId = a.Id };
        var unnamed = Workspace(null, "ws-bbbbbbbb", Note("Loose"));

        TestReam.Repo(root).Save(Doc(unnamed.Id, personal, unnamed));

        var loaded = TestReam.Repo(root).Load();

        Assert.False(loaded.IsFirstRun);
        Assert.Equal(unnamed.Id, loaded.CurrentWorkspaceId);
        Assert.Equal(["Personal", null], loaded.Workspaces.Select(w => w.Name));

        var loadedPersonal = loaded.Workspaces[0];
        Assert.Equal(personal.Id, loadedPersonal.Id);
        Assert.Equal(a.Id, loadedPersonal.FocusedNoteId);
        Assert.Equal([a.Id, b.Id], loadedPersonal.Notes.Select(n => n.Id));
        Assert.Equal("milk\neggs", loadedPersonal.Notes[0].Body);
        Assert.Equal(1d / 3d, loadedPersonal.Notes[0].WidthFraction, 3);
        Assert.Equal(1.0, loadedPersonal.Notes[1].WidthFraction, 3);
        Assert.True(loadedPersonal.Notes[1].IsFullscreen);
        Assert.Equal("Ideas", loadedPersonal.Notes[1].Title);
    }

    [Fact]
    public void Save_WritesTheDocumentedLayoutAndLeavesNoTempFiles()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("Hello", "body");

        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));

        Assert.True(File.Exists(Path.Combine(root, TestReam.FileName)));
        Assert.True(File.Exists(Path.Combine(root, "ws-11111111", "layout.reamlayout")));
        Assert.Equal("body", File.ReadAllText(Path.Combine(root, "ws-11111111", note.Id.ToString("N") + ".reamnote")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Layout_StoresWidthAsReadableText()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");

        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", Note("N", width: 2d / 3d))));

        string json = File.ReadAllText(Path.Combine(root, "ws-11111111", "layout.reamlayout"));
        Assert.Contains("\"widthFraction\": 0.6667", json);
        Assert.DoesNotContain("\"width\":", json);
    }

    [Theory]
    [InlineData("\"width\": \"twoThirds\"", 2d / 3d)]
    [InlineData("\"width\": \"oneThird\"", 1d / 3d)]
    [InlineData("\"width\": \"full\"", 1.0)]
    [InlineData("\"width\": \"half\"", 0.5)]
    [InlineData("\"widthFraction\": 0.4", 0.4)]
    [InlineData("\"widthFraction\": 0.4, \"width\": \"full\"", 0.4)]
    [InlineData("\"widthFraction\": 7", 1.0)]
    [InlineData("\"widthFraction\": 0.01", 0.15)]
    [InlineData("\"other\": 1", 0.5)]
    public void StoredWidth_LoadsFromEitherFormat_AndStaysInRange(string widthJson, double expected)
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("N", "body");
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.WriteAllText(
            Path.Combine(root, "ws-11111111", "layout.reamlayout"),
            $$"""{ "schemaVersion": 1, "notes": [ { "noteId": "{{note.Id}}", "fileName": "{{note.Id:N}}.reamnote", "title": "N", {{widthJson}} } ] }""");

        var loaded = TestReam.Repo(root).Load().Workspaces[0].Notes[0];

        Assert.Equal(expected, loaded.WidthFraction, 4);
    }

    [Fact]
    public void FreeformWidth_RoundTrips()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", Note("N", width: 0.37256))));

        var loaded = TestReam.Repo(root).Load().Workspaces[0].Notes[0];

        Assert.Equal(0.3726, loaded.WidthFraction, 4);
    }

    [Fact]
    public void RemovedNote_IsMovedToTrashNotDeleted()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var keep = Note("Keep", "k");
        var drop = Note("Drop", "precious");
        var repo = TestReam.Repo(root);
        var ws = Workspace("W", "ws-11111111", keep, drop);
        repo.Save(Doc(null, ws));

        repo.Save(Doc(null, ws with { Notes = [keep] }));

        string file = drop.Id.ToString("N") + ".reamnote";
        Assert.False(File.Exists(Path.Combine(root, "ws-11111111", file)));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(root, ".trash", "ws-11111111", file)));
        Assert.Equal([keep.Id], TestReam.Repo(root).Load().Workspaces[0].Notes.Select(n => n.Id));
    }

    [Fact]
    public void NoteMovedBetweenWorkspaces_MovesItsFile()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var moving = Note("Mover", "content");
        var source = Workspace("From", "ws-11111111", moving);
        var target = Workspace("To", "ws-22222222", Note("Resident"));
        var repo = TestReam.Repo(root);
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
        var repo = TestReam.Repo(root);
        repo.Save(Doc(null, keep, gone));
        Assert.True(Directory.Exists(Path.Combine(root, "ws-22222222")));

        repo.Save(Doc(null, keep));

        Assert.False(Directory.Exists(Path.Combine(root, "ws-22222222")));
        Assert.Single(TestReam.Repo(root).Load().Workspaces);
    }

    [Fact]
    public void MissingLayout_IsRebuiltFromNoteFiles()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("First line title", "First line title\nmore");
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.Delete(Path.Combine(root, "ws-11111111", "layout.reamlayout"));

        var loaded = TestReam.Repo(root).Load();

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
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", known)));

        var stray = Guid.NewGuid();
        File.WriteAllText(Path.Combine(root, "ws-11111111", stray.ToString("N") + ".reamnote"), "found me");

        var notes = TestReam.Repo(root).Load().Workspaces[0].Notes;

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Id == stray && n.Body == "found me");
    }

    [Fact]
    public void CorruptMetadata_IsSetAsideAndWorkspaceFoldersAreAdopted()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", Note("Survivor"))));
        File.WriteAllText(Path.Combine(root, TestReam.FileName), "{ this is not json");

        var loaded = TestReam.Repo(root).Load();

        Assert.False(File.Exists(Path.Combine(root, TestReam.FileName)));
        Assert.Single(Directory.GetFiles(root, TestReam.FileName + ".corrupt-*"));
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
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.WriteAllText(Path.Combine(root, "ws-11111111", "layout.reamlayout"), "garbage");

        var loaded = TestReam.Repo(root).Load();

        Assert.Equal(note.Id, Assert.Single(loaded.Workspaces[0].Notes).Id);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "ws-11111111"), "layout.reamlayout.corrupt-*"));
    }

    [Fact]
    public void MetadataWithPathTraversal_IsIgnored()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(dir.Combine("outside"));
        File.WriteAllText(
            Path.Combine(root, TestReam.FileName),
            """{ "schemaVersion": 1, "workspaces": [ { "id": "6c1f4d2e-0000-4000-8000-000000000001", "folderName": "..\\outside", "order": 0 } ] }""");

        var loaded = TestReam.Repo(root).Load();

        Assert.Empty(loaded.Workspaces);
    }

    [Fact]
    public void FileFromNewerVersion_IsRefusedRatherThanOverwritten()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, TestReam.FileName), """{ "schemaVersion": 99, "workspaces": [] }""");

        Assert.Throws<InvalidDataException>(() => TestReam.Repo(root).Load());
        Assert.True(File.Exists(Path.Combine(root, TestReam.FileName)));
    }

    [Fact]
    public void EditedBody_IsWrittenOnNextSave()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("N", "v1");
        var repo = TestReam.Repo(root);
        var ws = Workspace("W", "ws-11111111", note);
        repo.Save(Doc(null, ws));

        repo.Save(Doc(null, ws with { Notes = [note with { Body = "v2" }] }));

        Assert.Equal("v2", TestReam.Repo(root).Load().Workspaces[0].Notes[0].Body);
    }

    [Fact]
    public void RebuiltTitle_ComesFromTheTextOfAFormattedNote()
    {
        using var dir = new TempDir();
        string root = dir.Combine("ReemDocuments");
        var note = Note("ignored", """<ReamNote schemaVersion="1"><Doc><P><R b="1">Real title</R></P><P><R>more</R></P></Doc></ReamNote>""");
        TestReam.Repo(root).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.Delete(Path.Combine(root, "ws-11111111", "layout.reamlayout"));

        var recovered = TestReam.Repo(root).Load().Workspaces[0].Notes[0];

        Assert.Equal("Real title", recovered.Title);
    }
}
