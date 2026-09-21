using System.Text.Json;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class LegacyConverterTests
{
    private static readonly DateTime Stamp = new(2026, 3, 4, 5, 6, 7);
    private const string StampText = "20260304-050607";

    private static readonly Guid WorkspaceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid WorkspaceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid NoteA = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid NoteB = Guid.Parse("22222222-0000-0000-0000-00000000000b");

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private const string BodyA = "<ReamNote><Doc><P><R>hello</R></P></Doc></ReamNote>";

    /// <summary>An old-format ream: two workspaces, a note in each, an image, and a trashed note.</summary>
    private static void BuildLegacy(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "metadata.json"), $$"""
            {
              "schemaVersion": 1,
              "workspaces": [
                { "id": "{{WorkspaceA}}", "name": "W", "folderName": "ws-11111111", "order": 0 },
                { "id": "{{WorkspaceB}}", "folderName": "ws-22222222", "order": 1 }
              ],
              "currentWorkspaceId": "{{WorkspaceB}}"
            }
            """);

        AddLegacyWorkspace(folder, "ws-11111111", NoteA, "First", BodyA);
        AddLegacyWorkspace(folder, "ws-22222222", NoteB, "Second", "plain text");

        string assets = Path.Combine(folder, "ws-11111111", "assets", NoteA.ToString("N"));
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, "pic.png"), Png);

        string trash = Path.Combine(folder, ".trash", "ws-11111111");
        Directory.CreateDirectory(trash);
        File.WriteAllText(Path.Combine(trash, "gone.reamnote"), "trashed");
    }

    private static void AddLegacyWorkspace(string folder, string workspace, Guid noteId, string title, string body)
    {
        string directory = Path.Combine(folder, workspace);
        Directory.CreateDirectory(directory);
        string fileName = noteId.ToString("N") + ".reamnote";
        File.WriteAllText(Path.Combine(directory, fileName), body);
        File.WriteAllText(Path.Combine(directory, "layout.json"), $$"""
            {
              "schemaVersion": 1,
              "notes": [ { "noteId": "{{noteId}}", "fileName": "{{fileName}}", "title": "{{title}}", "widthFraction": 0.5 } ],
              "focusedNoteId": "{{noteId}}"
            }
            """);
    }

    private static string[] Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] Backups(string parent) =>
        Directory.GetDirectories(parent, "*-backup-*");

    [Fact]
    public void Convert_RenamesFilesAndMakesAFullBackup()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        string original = Path.Combine(folder, "ws-11111111", NoteA.ToString("N") + ".reamnote");
        byte[] noteBytes = File.ReadAllBytes(original);
        string[] before = Files(folder);

        var result = LegacyConverter.ConvertIfNeeded(folder, Stamp);

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(folder, "Notes.ream"), result.ReamFilePath);
        Assert.True(File.Exists(result.ReamFilePath));
        Assert.False(File.Exists(Path.Combine(folder, "metadata.json")));
        Assert.Equal(ReamPaths.SameFolder, JsonDocument.Parse(File.ReadAllText(result.ReamFilePath)).RootElement.GetProperty("dataFolder").GetString());

        foreach (string workspace in new[] { "ws-11111111", "ws-22222222" })
        {
            Assert.False(File.Exists(Path.Combine(folder, workspace, "layout.json")));
            Assert.True(File.Exists(Path.Combine(folder, workspace, "layout.reamlayout")));
        }

        // Notes, images and the trash are untouched.
        Assert.Equal(noteBytes, File.ReadAllBytes(original));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(folder, "ws-11111111", "assets", NoteA.ToString("N"), "pic.png")));
        Assert.Equal("trashed", File.ReadAllText(Path.Combine(folder, ".trash", "ws-11111111", "gone.reamnote")));

        // The backup is the folder as it was, in a sibling.
        Assert.Equal(Path.Combine(dir.Path, "Notes-backup-" + StampText), result.BackupFolder);
        Assert.Equal(before, Files(result.BackupFolder!));
        Assert.Equal(noteBytes, File.ReadAllBytes(Path.Combine(result.BackupFolder!, "ws-11111111", NoteA.ToString("N") + ".reamnote")));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(result.BackupFolder!, "ws-11111111", "assets", NoteA.ToString("N"), "pic.png")));
        Assert.Empty(Directory.GetDirectories(dir.Path, "*.partial"));
    }

    [Fact]
    public void Convert_KeepsTheWorkspaceListAndTheConvertedFolderLoads()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);

        var result = LegacyConverter.ConvertIfNeeded(folder, Stamp)!;

        var repository = new DocumentRepository(result.ReamFilePath);
        Assert.Equal("Notes", repository.Name);
        Assert.Equal(Path.GetFullPath(folder), repository.Root);

        var snapshot = repository.Load();
        Assert.False(snapshot.IsFirstRun);
        Assert.Equal(WorkspaceB, snapshot.CurrentWorkspaceId);
        Assert.Equal(["W", null], snapshot.Workspaces.Select(w => w.Name));
        Assert.Equal([WorkspaceA, WorkspaceB], snapshot.Workspaces.Select(w => w.Id));
        Assert.Equal(["ws-11111111", "ws-22222222"], snapshot.Workspaces.Select(w => w.FolderName));

        var first = Assert.Single(snapshot.Workspaces[0].Notes);
        Assert.Equal(NoteA, first.Id);
        Assert.Equal("First", first.Title);
        Assert.Equal(BodyA, first.Body);
        Assert.Equal(0.5, first.WidthFraction, 3);
        Assert.Equal(NoteA, snapshot.Workspaces[0].FocusedNoteId);

        var second = Assert.Single(snapshot.Workspaces[1].Notes);
        Assert.Equal(NoteB, second.Id);
        Assert.Equal("plain text", second.Body);
    }

    [Fact]
    public void Convert_SecondRunDoesNothing()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);

        Assert.NotNull(LegacyConverter.ConvertIfNeeded(folder, Stamp));
        string[] afterFirst = Files(folder);

        Assert.False(LegacyConverter.NeedsConversion(folder));
        Assert.Null(LegacyConverter.ConvertIfNeeded(folder, Stamp.AddMinutes(1)));
        Assert.Equal(afterFirst, Files(folder));
        Assert.Single(Backups(dir.Path));
    }

    [Fact]
    public void Resume_FinishesRenamingWithoutANewBackup()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        var first = LegacyConverter.ConvertIfNeeded(folder, Stamp)!;

        // Undo the second workspace's rename, as if the run had stopped before reaching it.
        string second = Path.Combine(folder, "ws-22222222");
        File.Move(Path.Combine(second, "layout.reamlayout"), Path.Combine(second, "layout.json"));

        Assert.True(LegacyConverter.NeedsConversion(folder));
        var resumed = LegacyConverter.ConvertIfNeeded(folder, Stamp.AddMinutes(5));

        Assert.NotNull(resumed);
        Assert.Null(resumed.BackupFolder);
        Assert.Equal(first.ReamFilePath, resumed.ReamFilePath);
        Assert.Single(Backups(dir.Path));
        Assert.False(File.Exists(Path.Combine(second, "layout.json")));
        Assert.True(File.Exists(Path.Combine(second, "layout.reamlayout")));

        var snapshot = new DocumentRepository(resumed.ReamFilePath).Load();
        Assert.Equal(["W", null], snapshot.Workspaces.Select(w => w.Name));
        Assert.Equal(NoteB, Assert.Single(snapshot.Workspaces[1].Notes).Id);

        Assert.False(LegacyConverter.NeedsConversion(folder));
        Assert.Null(LegacyConverter.ConvertIfNeeded(folder, Stamp.AddMinutes(6)));
    }

    [Fact]
    public void Resume_HalfWrittenStateLoadsBeforeItIsFinished()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        var converted = LegacyConverter.ConvertIfNeeded(folder, Stamp)!;

        string second = Path.Combine(folder, "ws-22222222");
        File.Move(Path.Combine(second, "layout.reamlayout"), Path.Combine(second, "layout.json"));

        // The repository reads the leftover layout.json by itself.
        var snapshot = new DocumentRepository(converted.ReamFilePath).Load();
        Assert.Equal(NoteB, Assert.Single(snapshot.Workspaces[1].Notes).Id);
    }

    [Fact]
    public void Resume_MetadataLeftBehindIsNotTouched()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        string metadata = Path.Combine(folder, "metadata.json");
        string text = File.ReadAllText(metadata);
        LegacyConverter.ConvertIfNeeded(folder, Stamp);

        // A run that stopped after writing the .ream but before deleting metadata.json.
        File.WriteAllText(metadata, text);

        Assert.False(LegacyConverter.NeedsConversion(folder));
        Assert.Null(LegacyConverter.ConvertIfNeeded(folder, Stamp.AddMinutes(1)));
        Assert.Equal(text, File.ReadAllText(metadata));
    }

    [Fact]
    public void Resume_LegacyLayoutBesideAReadableNewOneIsDropped()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        LegacyConverter.ConvertIfNeeded(folder, Stamp);

        string workspace = Path.Combine(folder, "ws-11111111");
        string current = File.ReadAllText(Path.Combine(workspace, "layout.reamlayout"));
        File.WriteAllText(Path.Combine(workspace, "layout.json"), "{ \"notes\": [] }");

        Assert.True(LegacyConverter.NeedsConversion(folder));
        Assert.Null(LegacyConverter.ConvertIfNeeded(folder, Stamp)!.BackupFolder);

        Assert.False(File.Exists(Path.Combine(workspace, "layout.json")));
        Assert.Equal(current, File.ReadAllText(Path.Combine(workspace, "layout.reamlayout")));
    }

    [Fact]
    public void Resume_LegacyLayoutBesideAnUnreadableNewOneKeepsBoth()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        LegacyConverter.ConvertIfNeeded(folder, Stamp);

        string workspace = Path.Combine(folder, "ws-11111111");
        File.WriteAllText(Path.Combine(workspace, "layout.reamlayout"), "{ not json");
        File.WriteAllText(Path.Combine(workspace, "layout.json"), "{ \"notes\": [] }");

        Assert.False(LegacyConverter.NeedsConversion(folder));
        Assert.Null(LegacyConverter.ConvertIfNeeded(folder, Stamp));
        Assert.True(File.Exists(Path.Combine(workspace, "layout.json")));
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(workspace, "layout.reamlayout")));
    }

    [Fact]
    public void Convert_CorruptMetadataIsSetAsideAndTheWorkspacesAreAdopted()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        File.WriteAllText(Path.Combine(folder, "metadata.json"), "{ this is not json");

        var result = LegacyConverter.ConvertIfNeeded(folder, Stamp);

        Assert.NotNull(result);
        Assert.False(File.Exists(Path.Combine(folder, "metadata.json")));
        Assert.Equal("{ this is not json", File.ReadAllText(Path.Combine(folder, "metadata.json.corrupt-20260304050607")));

        var ream = JsonDocument.Parse(File.ReadAllText(result.ReamFilePath)).RootElement;
        Assert.Equal(ReamPaths.SameFolder, ream.GetProperty("dataFolder").GetString());
        Assert.Equal(0, ream.GetProperty("workspaces").GetArrayLength());

        Assert.NotNull(result.BackupFolder);
        Assert.Equal("{ this is not json", File.ReadAllText(Path.Combine(result.BackupFolder, "metadata.json")));

        var snapshot = new DocumentRepository(result.ReamFilePath).Load();
        Assert.Equal(2, snapshot.Workspaces.Count);
        Assert.Equal([NoteA, NoteB], snapshot.Workspaces.Select(w => Assert.Single(w.Notes).Id).Order());

        Assert.False(LegacyConverter.NeedsConversion(folder));
        Assert.Null(LegacyConverter.ConvertIfNeeded(folder, Stamp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    public void Convert_MetadataOfTheWrongShapeCountsAsCorrupt(string text)
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        File.WriteAllText(Path.Combine(folder, "metadata.json"), text);

        var result = LegacyConverter.ConvertIfNeeded(folder, Stamp);

        Assert.NotNull(result);
        Assert.True(File.Exists(Path.Combine(folder, "metadata.json.corrupt-20260304050607")));
        Assert.Equal(2, new DocumentRepository(result.ReamFilePath).Load().Workspaces.Count);
    }

    [Fact]
    public void Convert_MetadataFromANewerVersionThrowsAndChangesNothing()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        File.WriteAllText(Path.Combine(folder, "metadata.json"), "{ \"schemaVersion\": 99, \"workspaces\": [] }");
        string[] before = Files(folder);

        Assert.Throws<InvalidDataException>(() => LegacyConverter.ConvertIfNeeded(folder, Stamp));

        Assert.Equal(before, Files(folder));
        Assert.Equal("{ \"schemaVersion\": 99, \"workspaces\": [] }", File.ReadAllText(Path.Combine(folder, "metadata.json")));
        Assert.Empty(Backups(dir.Path));
        Assert.Empty(Directory.GetDirectories(dir.Path, "*.partial"));
    }

    [Fact]
    public void Convert_FailedBackupThrowsAndLeavesTheOriginalAlone()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        string[] before = Files(folder);

        // A note held open with no sharing cannot be copied.
        string locked = Path.Combine(folder, "ws-22222222", NoteB.ToString("N") + ".reamnote");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => LegacyConverter.ConvertIfNeeded(folder, Stamp));
        }

        Assert.Equal(before, Files(folder));
        Assert.True(File.Exists(Path.Combine(folder, "metadata.json")));
        Assert.True(File.Exists(Path.Combine(folder, "ws-11111111", "layout.json")));
        Assert.Empty(Backups(dir.Path));
        Assert.Empty(Directory.GetDirectories(dir.Path, "*.partial"));

        // With the lock gone the same folder converts normally.
        Assert.NotNull(LegacyConverter.ConvertIfNeeded(folder, Stamp));
    }

    [Fact]
    public void Convert_FolderNameThatCannotBeAReamNameFallsBackToReam()
    {
        using var dir = new TempDir();
        string folder = dir.Combine(new string('x', 101));
        BuildLegacy(folder);

        var result = LegacyConverter.ConvertIfNeeded(folder, Stamp);

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(folder, "Ream.ream"), result.ReamFilePath);
        Assert.True(File.Exists(result.ReamFilePath));
        Assert.Equal(2, new DocumentRepository(result.ReamFilePath).Load().Workspaces.Count);
    }

    [Fact]
    public void Convert_ExistingBackupNameGetsANumberedSuffix()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        string taken = dir.Combine("Notes-backup-" + StampText);
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "keep.txt"), "mine");
        Directory.CreateDirectory(dir.Combine("Notes-backup-" + StampText + " (2)"));

        var result = LegacyConverter.ConvertIfNeeded(folder, Stamp);

        Assert.NotNull(result);
        string backup = result.BackupFolder!;
        Assert.Equal(dir.Combine("Notes-backup-" + StampText + " (3)"), backup);
        Assert.True(File.Exists(Path.Combine(backup, "metadata.json")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(taken, "keep.txt")));
        Assert.Equal(["keep.txt"], Files(taken));
    }

    [Fact]
    public void Convert_OnlyTouchesDirectWorkspaceFolders()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("Notes");
        BuildLegacy(folder);

        string other = Path.Combine(folder, "other");
        string nested = Path.Combine(folder, "ws-11111111", "ws-inner");
        Directory.CreateDirectory(other);
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(other, "layout.json"), "{}");
        File.WriteAllText(Path.Combine(nested, "layout.json"), "{}");

        LegacyConverter.ConvertIfNeeded(folder, Stamp);

        Assert.True(File.Exists(Path.Combine(other, "layout.json")));
        Assert.True(File.Exists(Path.Combine(nested, "layout.json")));
        Assert.False(File.Exists(Path.Combine(other, "layout.reamlayout")));
        Assert.False(File.Exists(Path.Combine(nested, "layout.reamlayout")));
    }

    [Fact]
    public void NeedsConversion_ForNothingLegacyConvertedAndMissingFolders()
    {
        using var dir = new TempDir();

        Assert.False(LegacyConverter.NeedsConversion(dir.Path));
        Assert.Null(LegacyConverter.ConvertIfNeeded(dir.Path, Stamp));

        string folder = dir.Combine("Notes");
        BuildLegacy(folder);
        Assert.True(LegacyConverter.NeedsConversion(folder));

        LegacyConverter.ConvertIfNeeded(folder, Stamp);
        Assert.False(LegacyConverter.NeedsConversion(folder));

        string missing = dir.Combine("nope");
        Assert.False(LegacyConverter.NeedsConversion(missing));
        Assert.Null(LegacyConverter.ConvertIfNeeded(missing, Stamp));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void NeedsConversion_IsFalseForAFolderWithOnlyANewFormatReam()
    {
        using var dir = new TempDir();
        var repository = TestReam.Repo(dir.Path);
        repository.Save(new Ream.Core.Models.DocumentSnapshot([], null));

        Assert.True(File.Exists(TestReam.FileIn(dir.Path)));
        Assert.False(LegacyConverter.NeedsConversion(dir.Path));
        Assert.Null(LegacyConverter.ConvertIfNeeded(dir.Path, Stamp));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(dir.Path)!, Path.GetFileName(dir.Path) + "-backup-*"));
    }
}
