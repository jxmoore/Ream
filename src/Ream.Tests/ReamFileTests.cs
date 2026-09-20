using System.Text.Json;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>The .ream file and its data folder: where things go, what the file records, renames, recovery.</summary>
public class ReamFileTests
{
    private static NoteSnapshot Note(string title, string body = "", double width = 0.5) =>
        new(Guid.NewGuid(), title, body, width, false);

    private static WorkspaceSnapshot Workspace(string? name, string folder, params NoteSnapshot[] notes) =>
        new(Guid.NewGuid(), name, folder, notes, notes.Length > 0 ? notes[^1].Id : null);

    private static DocumentSnapshot Doc(Guid? current, params WorkspaceSnapshot[] workspaces) => new(workspaces, current);

    private static string NoteFile(NoteSnapshot note) => note.Id.ToString("N") + ".reamnote";

    private static string? RecordedDataFolder(string reamFile)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(reamFile));
        return json.RootElement.GetProperty("dataFolder").GetString();
    }

    private static string ReamJson(string dataFolder, string workspacesJson = "[]", int schemaVersion = 1) =>
        $$"""{ "schemaVersion": {{schemaVersion}}, "dataFolder": {{JsonSerializer.Serialize(dataFolder)}}, "workspaces": {{workspacesJson}} }""";

    // ---- where things go -------------------------------------------------------------------------------------------

    [Fact]
    public void Save_NewReam_CreatesTheFileBesideADataFolderNamedAfterIt()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Hello", "body");

        var repo = new DocumentRepository(file);
        repo.Save(Doc(null, Workspace("W", "ws-11111111", note)));

        Assert.Equal("Foo", repo.Name);
        Assert.Equal("Foo", repo.DataFolder);
        Assert.Equal(dir.Combine("Foo"), repo.Root);
        Assert.True(File.Exists(file));
        Assert.Equal("Foo", RecordedDataFolder(file));
        Assert.True(File.Exists(dir.Combine("Foo", "ws-11111111", "layout.reamlayout")));
        Assert.Equal("body", File.ReadAllText(dir.Combine("Foo", "ws-11111111", NoteFile(note))));

        // Nothing but the file and its folder beside each other, and no workspace folder outside the data folder.
        Assert.Equal([file], Directory.GetFiles(dir.Path));
        Assert.Equal([dir.Combine("Foo")], Directory.GetDirectories(dir.Path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Save_WithDataFolderDot_KeepsEverythingBesideTheFile()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Hello", "body");

        var repo = new DocumentRepository(file, ReamPaths.SameFolder);
        repo.Save(Doc(null, Workspace("W", "ws-11111111", note)));

        Assert.Equal(dir.Path, repo.Root);
        Assert.Equal(".", repo.DataFolder);
        Assert.Equal(".", RecordedDataFolder(file));
        Assert.True(File.Exists(dir.Combine("ws-11111111", "layout.reamlayout")));
        Assert.True(File.Exists(dir.Combine("ws-11111111", NoteFile(note))));
        Assert.False(Directory.Exists(dir.Combine("Foo")));
    }

    [Fact]
    public void Save_WithAnExplicitDataFolder_UsesItAndRecordsIt()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");

        var repo = new DocumentRepository(file, "Elsewhere");
        repo.Save(Doc(null, Workspace("W", "ws-11111111", Note("N"))));

        Assert.Equal("Elsewhere", RecordedDataFolder(file));
        Assert.True(File.Exists(dir.Combine("Elsewhere", "ws-11111111", "layout.reamlayout")));
        Assert.False(Directory.Exists(dir.Combine("Foo")));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWithTheSiblingDataFolder()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var a = Note("Groceries", "milk\neggs", 1d / 3d);
        var b = Note("Ideas", "big ideas", 1.0) with { IsFullscreen = true, CustomTitle = "Mine" };
        var personal = Workspace("Personal", "ws-aaaaaaaa", a, b) with { FocusedNoteId = a.Id };
        var unnamed = Workspace(null, "ws-bbbbbbbb", Note("Loose"));
        new DocumentRepository(file).Save(Doc(unnamed.Id, personal, unnamed));

        var loaded = new DocumentRepository(file).Load();

        Assert.False(loaded.IsFirstRun);
        Assert.Equal(unnamed.Id, loaded.CurrentWorkspaceId);
        Assert.Equal(["Personal", null], loaded.Workspaces.Select(w => w.Name));
        var first = loaded.Workspaces[0];
        Assert.Equal(personal.Id, first.Id);
        Assert.Equal(a.Id, first.FocusedNoteId);
        Assert.Equal([a.Id, b.Id], first.Notes.Select(n => n.Id));
        Assert.Equal("milk\neggs", first.Notes[0].Body);
        Assert.Equal(1d / 3d, first.Notes[0].WidthFraction, 3);
        Assert.True(first.Notes[1].IsFullscreen);
        Assert.Equal("Mine", first.Notes[1].CustomTitle);
    }

    // ---- the .ream is only rewritten when it changes -----------------------------------------------------------------

    [Fact]
    public void Save_UnchangedSnapshot_DoesNotRewriteTheReamFile()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(file);
        var snapshot = Doc(null, Workspace("W", "ws-11111111", Note("N", "body")));
        repo.Save(snapshot);

        var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, old);
        string before = File.ReadAllText(file);

        repo.Save(snapshot);

        Assert.Equal(old, File.GetLastWriteTimeUtc(file));
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public void Save_ChangedWorkspaceList_RewritesTheReamFile()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(file);
        var workspace = Workspace("Before", "ws-11111111", Note("N", "body"));
        repo.Save(Doc(null, workspace));
        File.SetLastWriteTimeUtc(file, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));

        repo.Save(Doc(null, workspace with { Name = "After" }));

        Assert.NotEqual(2001, File.GetLastWriteTimeUtc(file).Year);
        Assert.Equal("After", new DocumentRepository(file).Load().Workspaces[0].Name);
    }

    [Fact]
    public void Save_RecreatesAReamFileThatWasDeletedUnderneathIt()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(file);
        var snapshot = Doc(null, Workspace("W", "ws-11111111", Note("N", "body")));
        repo.Save(snapshot);
        File.Delete(file);

        repo.Save(snapshot);

        Assert.True(File.Exists(file));
    }

    // ---- renaming and moving ---------------------------------------------------------------------------------------

    [Fact]
    public void RenamedReamFile_StillLoadsEverythingBecauseTheFileRecordsItsDataFolder()
    {
        using var dir = new TempDir();
        var note = Note("Kept", "text");
        var workspace = Workspace("W", "ws-11111111", note);
        new DocumentRepository(dir.Combine("Foo.ream")).Save(Doc(workspace.Id, workspace));

        File.Move(dir.Combine("Foo.ream"), dir.Combine("Bar.ream"));
        var repo = new DocumentRepository(dir.Combine("Bar.ream"));
        var loaded = repo.Load();

        Assert.Equal("Bar", repo.Name);
        Assert.Equal("Foo", repo.DataFolder);
        Assert.Equal(dir.Combine("Foo"), repo.Root);
        var loadedWorkspace = Assert.Single(loaded.Workspaces);
        Assert.Equal("W", loadedWorkspace.Name);
        Assert.Equal(note.Id, Assert.Single(loadedWorkspace.Notes).Id);
        Assert.Equal("text", loadedWorkspace.Notes[0].Body);
    }

    [Fact]
    public void RenamedReamFile_KeepsSavingIntoTheOriginalDataFolder()
    {
        using var dir = new TempDir();
        new DocumentRepository(dir.Combine("Foo.ream")).Save(Doc(null, Workspace("W", "ws-11111111", Note("N", "v1"))));
        File.Move(dir.Combine("Foo.ream"), dir.Combine("Bar.ream"));

        var repo = new DocumentRepository(dir.Combine("Bar.ream"));
        var loaded = repo.Load();
        var edited = loaded.Workspaces[0] with { Notes = [loaded.Workspaces[0].Notes[0] with { Body = "v2" }] };
        repo.Save(loaded with { Workspaces = [edited] });

        Assert.False(Directory.Exists(dir.Combine("Bar")));
        Assert.Equal("Foo", RecordedDataFolder(dir.Combine("Bar.ream")));
        Assert.Equal("v2", new DocumentRepository(dir.Combine("Bar.ream")).Load().Workspaces[0].Notes[0].Body);
    }

    [Fact]
    public void RenamedReamFile_WithDataFolderDot_StillLoads()
    {
        using var dir = new TempDir();
        var note = Note("Kept", "text");
        new DocumentRepository(dir.Combine("Foo.ream"), ReamPaths.SameFolder).Save(Doc(null, Workspace("W", "ws-11111111", note)));

        File.Move(dir.Combine("Foo.ream"), dir.Combine("Bar.ream"));
        var repo = new DocumentRepository(dir.Combine("Bar.ream"));

        Assert.Equal(dir.Path, repo.Root);
        Assert.Equal(note.Id, Assert.Single(Assert.Single(repo.Load().Workspaces).Notes).Id);
    }

    [Fact]
    public void DataFolderInTheFile_WinsOverTheConstructorArgument()
    {
        using var dir = new TempDir();
        new DocumentRepository(dir.Combine("Foo.ream")).Save(Doc(null, Workspace("W", "ws-11111111", Note("N", "x"))));

        var repo = new DocumentRepository(dir.Combine("Foo.ream"), "Other");

        Assert.Equal("Foo", repo.DataFolder);
        Assert.Equal(dir.Combine("Foo"), repo.Root);
        Assert.Single(Assert.Single(repo.Load().Workspaces).Notes);
        Assert.False(Directory.Exists(dir.Combine("Other")));
    }

    [Fact]
    public void DotInTheFile_WinsOverTheConstructorArgument()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("ws-11111111"));
        File.WriteAllText(dir.Combine("Foo.ream"), ReamJson("."));

        var repo = new DocumentRepository(dir.Combine("Foo.ream"), "Other");

        Assert.Equal(".", repo.DataFolder);
        Assert.Equal(dir.Path, repo.Root);
    }

    [Fact]
    public void ReamFileWithoutADataFolder_UsesTheConstructorArgumentThenTheDefault()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("A.ream"), """{ "schemaVersion": 1, "workspaces": [] }""");
        File.WriteAllText(dir.Combine("B.ream"), """{ "schemaVersion": 1, "workspaces": [] }""");

        Assert.Equal("Given", new DocumentRepository(dir.Combine("A.ream"), "Given").DataFolder);
        Assert.Equal("B", new DocumentRepository(dir.Combine("B.ream")).DataFolder);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData(@"..\outside")]
    [InlineData("")]
    [InlineData("a:b")]
    public void UnsafeDataFolderInTheFile_MakesTheConstructorThrow(string dataFolder)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("Foo.ream"), ReamJson(dataFolder));

        Assert.Throws<InvalidDataException>(() => new DocumentRepository(dir.Combine("Foo.ream")));
        Assert.Equal(ReamJson(dataFolder), File.ReadAllText(dir.Combine("Foo.ream")));
    }

    [Theory]
    [InlineData("Foo.txt")]
    [InlineData("Foo")]
    [InlineData("Foo.reamnote")]
    [InlineData("Foo.reamlayout")]
    [InlineData("Foo.ream.tmp")]
    [InlineData("metadata.json")]
    public void APathThatIsNotAReamFile_IsRefused(string fileName)
    {
        using var dir = new TempDir();

        Assert.Throws<ArgumentException>(() => new DocumentRepository(dir.Combine(fileName)));
        Assert.Throws<ArgumentException>(() => new DocumentRepository(dir.Combine(fileName), ReamPaths.SameFolder));
    }

    [Fact]
    public void ExtensionIsMatchedIgnoringCase()
    {
        using var dir = new TempDir();

        var repo = new DocumentRepository(dir.Combine("Foo.REAM"));

        Assert.Equal("Foo", repo.Name);
    }

    // ---- first run -------------------------------------------------------------------------------------------------

    [Fact]
    public void Load_NoFileAndNoFolders_IsFirstRun()
    {
        using var dir = new TempDir();

        var snapshot = new DocumentRepository(dir.Combine("Foo.ream")).Load();

        Assert.True(snapshot.IsFirstRun);
        Assert.Empty(snapshot.Workspaces);
        Assert.False(File.Exists(dir.Combine("Foo.ream")));
    }

    [Fact]
    public void Load_NoFileAndAnExistingEmptyFolder_IsStillFirstRun()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Foo"));

        Assert.True(new DocumentRepository(dir.Combine("Foo.ream")).Load().IsFirstRun);
        Assert.True(new DocumentRepository(dir.Combine("Foo.ream")).Load().IsFirstRun);
    }

    [Fact]
    public void SavedReamWithZeroWorkspaces_ReloadsAsNotFirstRun()
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Foo.ream"));
        repo.Save(Doc(null, Workspace("W", "ws-11111111", Note("N", "x"))));
        repo.Save(Doc(null));

        var loaded = new DocumentRepository(dir.Combine("Foo.ream")).Load();

        Assert.False(loaded.IsFirstRun);
        Assert.Empty(loaded.Workspaces);
    }

    [Fact]
    public void ReamThatWasNeverPopulated_ButSavedEmpty_IsNotFirstRun()
    {
        using var dir = new TempDir();
        new DocumentRepository(dir.Combine("Foo.ream")).Save(Doc(null));

        Assert.False(new DocumentRepository(dir.Combine("Foo.ream")).Load().IsFirstRun);
    }

    // ---- the legacy layout.json name -------------------------------------------------------------------------------

    [Fact]
    public void LegacyLayoutJson_IsReadWhenTheNewLayoutIsMissing_AndReplacedOnTheNextSave()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Old title", "body");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        string wsDir = dir.Combine("Foo", "ws-11111111");
        File.Delete(Path.Combine(wsDir, "layout.reamlayout"));
        File.WriteAllText(
            Path.Combine(wsDir, "layout.json"),
            $$"""{ "schemaVersion": 1, "notes": [ { "noteId": "{{note.Id}}", "fileName": "{{NoteFile(note)}}", "title": "From legacy", "widthFraction": 0.4 } ], "focusedNoteId": "{{note.Id}}" }""");

        var repo = new DocumentRepository(file);
        var loaded = repo.Load();

        var read = Assert.Single(loaded.Workspaces[0].Notes);
        Assert.Equal("From legacy", read.Title);
        Assert.Equal(0.4, read.WidthFraction, 4);
        Assert.Equal(note.Id, loaded.Workspaces[0].FocusedNoteId);
        Assert.False(File.Exists(Path.Combine(wsDir, "layout.reamlayout")));

        repo.Save(loaded);

        Assert.True(File.Exists(Path.Combine(wsDir, "layout.reamlayout")));
        Assert.False(File.Exists(Path.Combine(wsDir, "layout.json")));
        var reloaded = Assert.Single(new DocumentRepository(file).Load().Workspaces[0].Notes);
        Assert.Equal("From legacy", reloaded.Title);
        Assert.Equal(0.4, reloaded.WidthFraction, 4);
    }

    [Fact]
    public void WhenBothLayoutFilesExist_TheNewOneWins_AndTheOldOneIsRemovedOnSave()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("New title", "body", width: 0.6);
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        string wsDir = dir.Combine("Foo", "ws-11111111");
        File.WriteAllText(
            Path.Combine(wsDir, "layout.json"),
            $$"""{ "schemaVersion": 1, "notes": [ { "noteId": "{{note.Id}}", "fileName": "{{NoteFile(note)}}", "title": "Stale", "widthFraction": 0.2 } ] }""");

        var repo = new DocumentRepository(file);
        var loaded = repo.Load();

        var read = Assert.Single(loaded.Workspaces[0].Notes);
        Assert.Equal("New title", read.Title);
        Assert.Equal(0.6, read.WidthFraction, 4);

        repo.Save(loaded);

        Assert.True(File.Exists(Path.Combine(wsDir, "layout.reamlayout")));
        Assert.False(File.Exists(Path.Combine(wsDir, "layout.json")));
    }

    [Fact]
    public void CorruptNewLayout_FallsBackToTheLegacyOne()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Title", "body");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        string wsDir = dir.Combine("Foo", "ws-11111111");
        File.WriteAllText(Path.Combine(wsDir, "layout.reamlayout"), "garbage");
        File.WriteAllText(
            Path.Combine(wsDir, "layout.json"),
            $$"""{ "schemaVersion": 1, "notes": [ { "noteId": "{{note.Id}}", "fileName": "{{NoteFile(note)}}", "title": "Legacy", "widthFraction": 0.4 } ] }""");

        var read = Assert.Single(new DocumentRepository(file).Load().Workspaces[0].Notes);

        Assert.Equal("Legacy", read.Title);
        Assert.Single(Directory.GetFiles(wsDir, "layout.reamlayout.corrupt-*"));
    }

    // ---- crash leftovers -------------------------------------------------------------------------------------------

    [Fact]
    public void ReamTempBesideTheFile_IsPromotedWhenTheReamIsMissingAndTheTempIsComplete()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Kept", "text");
        var workspace = Workspace("Named", "ws-11111111", note);
        new DocumentRepository(file).Save(Doc(workspace.Id, workspace));
        File.Move(file, file + ".tmp"); // crashed after writing the temp, before the move

        var loaded = new DocumentRepository(file).Load();

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.False(Directory.Exists(dir.Combine("Foo", ".recovered")));
        var read = Assert.Single(loaded.Workspaces);
        Assert.Equal("Named", read.Name); // the name lives only in the .ream, so the promoted file was really read
        Assert.Equal(workspace.Id, loaded.CurrentWorkspaceId);
        Assert.Equal(note.Id, Assert.Single(read.Notes).Id);
    }

    [Fact]
    public void ReamTempBesideTheFile_IsPromotedWithDataFolderDotToo()
    {
        using var dir = new TempDir();
        var workspace = Workspace("Named", "ws-11111111", Note("Kept", "text"));
        var repo = TestReam.Repo(dir.Path);
        repo.Save(Doc(null, workspace));
        string file = TestReam.FileIn(dir.Path);
        File.Move(file, file + ".tmp");

        var loaded = TestReam.Repo(dir.Path).Load();

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Equal("Named", Assert.Single(loaded.Workspaces).Name);
    }

    [Fact]
    public void IncompleteReamTemp_IsSetAsideInTheDataFoldersRecoveredFolderNotDeleted()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Kept", "text");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        File.Delete(file);
        const string half = """{ "schemaVersion": 1, "dataFolder": "Foo", "workspa""";
        File.WriteAllText(file + ".tmp", half);

        var loaded = new DocumentRepository(file).Load();

        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        string aside = Assert.Single(Directory.GetFiles(dir.Combine("Foo", ".recovered"), "Foo.ream.tmp", SearchOption.AllDirectories));
        Assert.Equal(half, File.ReadAllText(aside));
        Assert.Equal(note.Id, Assert.Single(Assert.Single(loaded.Workspaces).Notes).Id); // the folders are adopted
    }

    [Fact]
    public void CompleteReamTemp_IsSetAsideNotPromoted_WhenTheRealFileExists()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        new DocumentRepository(file).Save(Doc(null, Workspace("Real", "ws-11111111", Note("N", "x"))));
        string real = File.ReadAllText(file);
        string other = ReamJson("Foo");
        File.WriteAllText(file + ".tmp", other);

        var loaded = new DocumentRepository(file).Load();

        Assert.Equal(real, File.ReadAllText(file));
        Assert.Equal("Real", Assert.Single(loaded.Workspaces).Name);
        Assert.False(File.Exists(file + ".tmp"));
        string aside = Assert.Single(Directory.GetFiles(dir.Combine("Foo", ".recovered"), "Foo.ream.tmp", SearchOption.AllDirectories));
        Assert.Equal(other, File.ReadAllText(aside));
    }

    [Fact]
    public void LeftoverTempsInsideWorkspaceFolders_AreStillRecovered()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var note = Note("Kept", "text");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", note)));
        string wsDir = dir.Combine("Foo", "ws-11111111");
        string noteFile = Path.Combine(wsDir, NoteFile(note));
        File.Move(noteFile, noteFile + ".tmp");

        var loaded = new DocumentRepository(file).Load();

        Assert.True(File.Exists(noteFile));
        Assert.Equal("text", Assert.Single(Assert.Single(loaded.Workspaces).Notes).Body);
    }

    [Fact]
    public void WithDataFolderDot_UnrelatedTempFilesInTheFolderAreLeftAlone()
    {
        using var dir = new TempDir();
        var repo = TestReam.Repo(dir.Path);
        repo.Save(Doc(null, Workspace("W", "ws-11111111", Note("N", "x"))));

        // The data folder is the user's own folder here: whatever else lives in it, or below it, is none of Ream's business.
        Directory.CreateDirectory(dir.Combine("Other project"));
        string strayTop = dir.Combine("download.tmp");
        string strayNested = dir.Combine("Other project", "scratch.json.tmp");
        File.WriteAllText(strayTop, "a");
        File.WriteAllText(strayNested, "b");

        TestReam.Repo(dir.Path).Load();

        Assert.Equal("a", File.ReadAllText(strayTop));
        Assert.Equal("b", File.ReadAllText(strayNested));
        Assert.False(Directory.Exists(dir.Combine(".recovered")));
    }

    // ---- adoption and damage ---------------------------------------------------------------------------------------

    [Fact]
    public void WorkspaceFoldersMissingFromTheReam_AreAdopted()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var listed = Workspace("Listed", "ws-11111111", Note("A", "a"));
        var stray = Workspace("Stray", "ws-22222222", Note("B", "b"));
        new DocumentRepository(file).Save(Doc(null, listed, stray));
        // Rewrite the file so it only knows the first workspace.
        File.WriteAllText(
            file,
            ReamJson("Foo", $$"""[ { "id": "{{listed.Id}}", "name": "Listed", "folderName": "ws-11111111", "order": 0 } ]"""));

        var loaded = new DocumentRepository(file).Load();

        Assert.Equal(2, loaded.Workspaces.Count);
        Assert.Equal(listed.Id, loaded.Workspaces[0].Id);
        Assert.Equal("Listed", loaded.Workspaces[0].Name);
        var adopted = loaded.Workspaces[1];
        Assert.Equal("ws-22222222", adopted.FolderName);
        Assert.Null(adopted.Name);
        Assert.Equal("b", Assert.Single(adopted.Notes).Body);
        Assert.False(loaded.IsFirstRun);
    }

    [Fact]
    public void WorkspaceFoldersAreAdopted_WhenTheReamListsNone()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", Note("A", "a"))));
        File.WriteAllText(file, ReamJson("Foo"));

        var loaded = new DocumentRepository(file).Load();

        Assert.Equal("a", Assert.Single(Assert.Single(loaded.Workspaces).Notes).Body);
    }

    [Fact]
    public void ReamListingAWorkspaceWhoseFolderIsGone_SkipsIt()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var real = Workspace("Real", "ws-11111111", Note("A", "a"));
        new DocumentRepository(file).Save(Doc(null, real));
        File.WriteAllText(
            file,
            ReamJson(
                "Foo",
                $$"""[ { "id": "{{real.Id}}", "name": "Real", "folderName": "ws-11111111", "order": 0 }, { "id": "{{Guid.NewGuid()}}", "name": "Ghost", "folderName": "ws-99999999", "order": 1 } ]"""));

        var loaded = new DocumentRepository(file).Load();

        Assert.Equal(["Real"], loaded.Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void CorruptReam_IsSetAsideBesideItAndWorkspaceFoldersAreAdopted()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", Note("Survivor", "s"))));
        File.WriteAllText(file, "{ this is not json");

        var loaded = new DocumentRepository(file).Load();

        Assert.False(File.Exists(file));
        string aside = Assert.Single(Directory.GetFiles(dir.Path, "Foo.ream.corrupt-*"));
        Assert.Equal("{ this is not json", File.ReadAllText(aside));
        Assert.Equal("Survivor", Assert.Single(Assert.Single(loaded.Workspaces).Notes).Title);
        Assert.False(loaded.IsFirstRun);
    }

    [Fact]
    public void CorruptReam_IsWrittenAnewOnTheNextSave()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        new DocumentRepository(file).Save(Doc(null, Workspace("W", "ws-11111111", Note("Survivor", "s"))));
        File.WriteAllText(file, "{ this is not json");

        var repo = new DocumentRepository(file);
        repo.Save(repo.Load());

        Assert.Equal("Foo", RecordedDataFolder(file));
        Assert.Equal("Survivor", Assert.Single(Assert.Single(new DocumentRepository(file).Load().Workspaces).Notes).Title);
    }

    [Fact]
    public void ReamFromANewerSchema_ThrowsAndIsLeftUntouched()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        string content = ReamJson("Foo", schemaVersion: 99);
        File.WriteAllText(file, content);
        var stamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        Assert.Throws<InvalidDataException>(() => new DocumentRepository(file).Load());

        Assert.Equal(content, File.ReadAllText(file));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*", SearchOption.AllDirectories));
    }

    // ---- notes and images ------------------------------------------------------------------------------------------

    [Fact]
    public void NoteMovedBetweenWorkspaces_KeepsItsImages()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var moving = Note("Mover", "content");
        var source = Workspace("From", "ws-11111111", moving);
        var target = Workspace("To", "ws-22222222", Note("Resident"));
        var repo = new DocumentRepository(file);
        repo.Save(Doc(null, source, target));
        byte[] png = [1, 2, 3, 4];
        string image = repo.SaveAsset("ws-11111111", moving.Id, png);

        repo.Save(Doc(null, source with { Notes = [] }, target with { Notes = [.. target.Notes, moving] }));

        string moved = repo.GetAssetPath("ws-22222222", moving.Id, image)!;
        Assert.NotNull(moved);
        Assert.Equal(dir.Combine("Foo", "ws-22222222", "assets", moving.Id.ToString("N"), image), moved);
        Assert.Equal(png, File.ReadAllBytes(moved));
        Assert.False(Directory.Exists(dir.Combine("Foo", "ws-11111111", "assets")));
        Assert.Equal("content", File.ReadAllText(dir.Combine("Foo", "ws-22222222", NoteFile(moving))));
        Assert.False(Directory.Exists(dir.Combine("Foo", ".trash")));

        // And a fresh repository over the same files still finds it.
        Assert.Equal(moved, new DocumentRepository(file).GetAssetPath("ws-22222222", moving.Id, image));
    }

    [Fact]
    public void RemovedNote_IsMovedIntoTheDataFoldersTrashNotDeleted()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");
        var keep = Note("Keep", "k");
        var drop = Note("Drop", "precious");
        var repo = new DocumentRepository(file);
        var workspace = Workspace("W", "ws-11111111", keep, drop);
        repo.Save(Doc(null, workspace));
        string image = repo.SaveAsset("ws-11111111", drop.Id, [9, 9, 9]);

        repo.Save(Doc(null, workspace with { Notes = [keep] }));

        Assert.False(File.Exists(dir.Combine("Foo", "ws-11111111", NoteFile(drop))));
        Assert.Equal("precious", File.ReadAllText(dir.Combine("Foo", ".trash", "ws-11111111", NoteFile(drop))));
        Assert.Equal([9, 9, 9], File.ReadAllBytes(dir.Combine("Foo", ".trash", "ws-11111111", "assets", drop.Id.ToString("N"), image)));
        Assert.False(Directory.Exists(dir.Combine(".trash"))); // trash lives with the data, not beside the .ream
        Assert.Equal([keep.Id], new DocumentRepository(file).Load().Workspaces[0].Notes.Select(n => n.Id));
    }

    // ---- several reams side by side --------------------------------------------------------------------------------

    [Fact]
    public void TwoReamsInTheSameFolder_DoNotMixTheirData()
    {
        using var dir = new TempDir();
        var aNote = Note("In A", "alpha");
        var bNote = Note("In B", "beta");
        // The same workspace folder name in both: only the data folder keeps them apart.
        var a = new DocumentRepository(dir.Combine("A.ream"));
        var b = new DocumentRepository(dir.Combine("B.ream"));
        a.Save(Doc(null, Workspace("Alpha", "ws-11111111", aNote)));
        b.Save(Doc(null, Workspace("Beta", "ws-11111111", bNote)));

        var loadedA = new DocumentRepository(dir.Combine("A.ream")).Load();
        var loadedB = new DocumentRepository(dir.Combine("B.ream")).Load();

        Assert.NotEqual(a.Root, b.Root);
        Assert.Equal(["Alpha"], loadedA.Workspaces.Select(w => w.Name));
        Assert.Equal(["Beta"], loadedB.Workspaces.Select(w => w.Name));
        Assert.Equal([aNote.Id], loadedA.Workspaces[0].Notes.Select(n => n.Id));
        Assert.Equal([bNote.Id], loadedB.Workspaces[0].Notes.Select(n => n.Id));
        Assert.Equal("alpha", loadedA.Workspaces[0].Notes[0].Body);
        Assert.Equal("beta", loadedB.Workspaces[0].Notes[0].Body);
        Assert.Equal(["A", "B"], Directory.GetDirectories(dir.Path).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public void RemovingANoteInOneReam_DoesNotTouchTheOther()
    {
        using var dir = new TempDir();
        var aNote = Note("In A", "alpha");
        var bNote = Note("In B", "beta");
        var a = new DocumentRepository(dir.Combine("A.ream"));
        var b = new DocumentRepository(dir.Combine("B.ream"));
        var aWorkspace = Workspace("Alpha", "ws-11111111", aNote);
        a.Save(Doc(null, aWorkspace));
        b.Save(Doc(null, Workspace("Beta", "ws-11111111", bNote)));

        a.Save(Doc(null, aWorkspace with { Notes = [] }));

        Assert.Equal("beta", File.ReadAllText(dir.Combine("B", "ws-11111111", NoteFile(bNote))));
        Assert.Empty(new DocumentRepository(dir.Combine("A.ream")).Load().Workspaces[0].Notes);
        Assert.Single(new DocumentRepository(dir.Combine("B.ream")).Load().Workspaces[0].Notes);
        Assert.False(Directory.Exists(dir.Combine("B", ".trash")));
    }
}
