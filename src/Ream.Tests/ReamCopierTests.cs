using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class ReamCopierTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static NoteSnapshot Note(string title, string body, double width = 0.5, bool fullscreen = false) =>
        new(Guid.NewGuid(), title, body, width, fullscreen);

    private static WorkspaceSnapshot Workspace(string? name, string folder, params NoteSnapshot[] notes) =>
        new(Guid.NewGuid(), name, folder, notes, notes.Length > 0 ? notes[^1].Id : null);

    /// <summary>Two workspaces, three notes and an image, saved through <paramref name="repo"/>.</summary>
    private static (DocumentSnapshot Doc, string ImageName, NoteSnapshot Imaged) Populate(DocumentRepository repo)
    {
        var a = Note("Groceries", "milk\neggs", 1d / 3d);
        var b = Note("Ideas", "big ideas", 1.0, fullscreen: true);
        var c = Note("Loose", "loose ends", 0.75);
        var personal = Workspace("Personal", "ws-aaaaaaaa", a, b) with { FocusedNoteId = a.Id };
        var unnamed = Workspace(null, "ws-bbbbbbbb", c);
        var doc = new DocumentSnapshot([personal, unnamed], unnamed.Id);

        repo.Save(doc);
        string image = repo.SaveAsset("ws-aaaaaaaa", a.Id, Png);
        return (doc, image, a);
    }

    private static string Describe(DocumentSnapshot snapshot) =>
        string.Join("\n", snapshot.Workspaces.SelectMany(w => new[] { $"W {w.Id} {w.Name} {w.FolderName} {w.FocusedNoteId}" }
            .Concat(w.Notes.Select(n => $"N {n.Id} {n.Title} {n.WidthFraction:0.####} {n.IsFullscreen} [{n.Body}]"))))
        + $"\ncurrent {snapshot.CurrentWorkspaceId}";

    /// <summary>Every file under a folder with its bytes, so before/after can be compared.</summary>
    private static Dictionary<string, string> Fingerprint(string folder) =>
        Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(
                p => Path.GetRelativePath(folder, p),
                p => File.Exists(p) ? Convert.ToHexString(File.ReadAllBytes(p)) : "<dir>");

    private static IEnumerable<string> RelativeFiles(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(folder, p));

    [Fact]
    public void Copy_SiblingLayout_LoadsIdenticallyAndKeepsImages()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(source);
        var (_, image, imaged) = Populate(repo);
        string expected = Describe(repo.Load());

        string dest = dir.Combine("out", "Bar.ream");
        Directory.CreateDirectory(dir.Combine("out"));

        string returned = ReamCopier.Copy(source, dest);

        Assert.Equal(dest, returned);
        var copy = new DocumentRepository(dest);
        Assert.Equal(dir.Combine("out", "Bar"), copy.Root);
        Assert.Equal("Bar", copy.DataFolder);
        Assert.Equal(expected, Describe(copy.Load()));

        string? asset = copy.GetAssetPath("ws-aaaaaaaa", imaged.Id, image);
        Assert.NotNull(asset);
        Assert.StartsWith(copy.Root, asset);
        Assert.Equal(Png, File.ReadAllBytes(asset));
    }

    [Fact]
    public void Copy_DataFolderIsNamedAfterTheDestinationEvenIfTheSourceDiffers()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(source, "SomethingElse");
        Populate(repo);
        Assert.Equal("SomethingElse", repo.DataFolder);

        string dest = dir.Combine("Bar.ream");
        ReamCopier.Copy(source, dest);

        Assert.True(Directory.Exists(dir.Combine("Bar", "ws-aaaaaaaa")));
        Assert.False(Directory.Exists(dir.Combine("SomethingElse", "Bar")));
        Assert.Equal("Bar", new DocumentRepository(dest).DataFolder);
        Assert.Equal(Describe(repo.Load()), Describe(new DocumentRepository(dest).Load()));
    }

    [Fact]
    public void Copy_SameFolderLayout_CopiesOnlyWorkspaceFolders()
    {
        using var dir = new TempDir();
        var repo = TestReam.Repo(dir.Path);
        var (_, image, imaged) = Populate(repo);
        string expected = Describe(repo.Load());

        // The data folder is the ream's own folder here, so unrelated things can sit right beside the workspaces.
        File.WriteAllText(dir.Combine("Other.ream"), "{}");
        File.WriteAllText(dir.Combine("Other.reamlayout"), "{}");
        File.WriteAllText(dir.Combine("notes.txt"), "mine");
        File.WriteAllText(dir.Combine("Test.ream.bak"), "backup");
        Directory.CreateDirectory(dir.Combine("Photos"));
        File.WriteAllText(dir.Combine("Photos", "cat.png"), "x");
        Directory.CreateDirectory(dir.Combine(".trash", "ws-aaaaaaaa"));
        File.WriteAllText(dir.Combine(".trash", "ws-aaaaaaaa", "gone.reamnote"), "old");
        Directory.CreateDirectory(dir.Combine(".recovered", "20240101000000"));
        File.WriteAllText(dir.Combine(".recovered", "20240101000000", "x.reamnote"), "aside");
        File.WriteAllText(dir.Combine("ws-aaaaaaaa", "half-written.reamnote.tmp"), "partial");
        File.WriteAllText(dir.Combine("stray.tmp"), "partial");

        string dest = dir.Combine("Bar.ream");
        ReamCopier.Copy(dir.Combine("Test.ream"), dest);

        string bar = dir.Combine("Bar");
        Assert.Equal(["ws-aaaaaaaa", "ws-bbbbbbbb"], Directory.GetDirectories(bar).Select(Path.GetFileName).Order());
        Assert.DoesNotContain(RelativeFiles(bar), f => f.EndsWith(".tmp"));
        Assert.Contains(RelativeFiles(bar), f => f.StartsWith(Path.Combine("ws-aaaaaaaa", "assets")));
        Assert.Equal(new[] { "Bar", "Bar.ream", "Other.ream", "Other.reamlayout", "notes.txt", "Photos", ".trash", ".recovered", "Test.ream", "Test.ream.bak", "stray.tmp", "ws-aaaaaaaa", "ws-bbbbbbbb" }.Order(),
            Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName).Order());

        var copy = new DocumentRepository(dest);
        Assert.Equal(expected, Describe(copy.Load()));
        Assert.NotNull(copy.GetAssetPath("ws-aaaaaaaa", imaged.Id, image));
    }

    [Fact]
    public void Copy_LeavesTrashAndRecoveredBehind()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(source);
        var (doc, _, _) = Populate(repo);

        // Closing a note trashes it.
        var personal = doc.Workspaces[0];
        repo.Save(doc with { Workspaces = [personal with { Notes = [personal.Notes[0]] }, doc.Workspaces[1]] });
        Assert.NotEmpty(Directory.GetFiles(dir.Combine("Foo", ".trash"), "*", SearchOption.AllDirectories));
        Directory.CreateDirectory(dir.Combine("Foo", ".recovered", "stamp"));
        File.WriteAllText(dir.Combine("Foo", ".recovered", "stamp", "x.reamnote"), "aside");

        string dest = dir.Combine("Bar.ream");
        ReamCopier.Copy(source, dest);

        Assert.Equal(["ws-aaaaaaaa", "ws-bbbbbbbb"], Directory.GetDirectories(dir.Combine("Bar")).Select(Path.GetFileName).Order());
        Assert.Equal(1, new DocumentRepository(dest).Load().Workspaces[0].Notes.Count);
    }

    [Fact]
    public void Copy_WritesTheDestinationReamWithTheNewDataFolderAndKeepsWorkspaces()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(source);
        var (doc, _, _) = Populate(repo);

        string dest = dir.Combine("Bar.ream");
        ReamCopier.Copy(source, dest);

        string json = File.ReadAllText(dest);
        Assert.Contains("\"dataFolder\": \"Bar\"", json);
        Assert.Contains(doc.CurrentWorkspaceId!.Value.ToString(), json);
        foreach (var workspace in doc.Workspaces) Assert.Contains(workspace.Id.ToString(), json);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Copy_ASourceWithNoRecordedDataFolder_UsesItsDefault()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        var repo = new DocumentRepository(source);
        Populate(repo);
        File.WriteAllText(source, "{ \"schemaVersion\": 1, \"workspaces\": [] }");

        ReamCopier.Copy(source, dir.Combine("Bar.ream"));

        Assert.Equal(["ws-aaaaaaaa", "ws-bbbbbbbb"], Directory.GetDirectories(dir.Combine("Bar")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Copy_NeverModifiesTheSource()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Src", "Foo.ream");
        var repo = new DocumentRepository(source);
        Populate(repo);
        Directory.CreateDirectory(dir.Combine("Src", "Foo", ".trash"));
        File.WriteAllText(dir.Combine("Src", "Foo", ".trash", "x.reamnote"), "old");
        var before = Fingerprint(dir.Combine("Src"));

        ReamCopier.Copy(source, dir.Combine("Bar.ream"));

        Assert.Equal(before, Fingerprint(dir.Combine("Src")));
    }

    [Fact]
    public void Copy_SameFolderSourceIsUnchangedWhenTheCopyLandsBesideIt()
    {
        using var dir = new TempDir();
        var repo = TestReam.Repo(dir.Path);
        Populate(repo);
        var before = Fingerprint(dir.Path);

        ReamCopier.Copy(dir.Combine("Test.ream"), dir.Combine("Bar.ream"));

        var after = Fingerprint(dir.Path);
        foreach (var (path, content) in before) Assert.Equal(content, after[path]);

        // ...and the source still loads exactly as a ream of its own: the copy is not adopted as a workspace.
        Assert.Equal(["ws-aaaaaaaa", "ws-bbbbbbbb"], TestReam.Repo(dir.Path).Load().Workspaces.Select(w => w.FolderName).Order());
    }

    [Fact]
    public void Copy_RefusesAnOccupiedDestinationFile()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        string dest = dir.Combine("Bar.ream");
        File.WriteAllText(dest, "precious");

        var ex = Assert.Throws<IOException>(() => ReamCopier.Copy(source, dest));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal("precious", File.ReadAllText(dest));
        Assert.False(Directory.Exists(dir.Combine("Bar")));
    }

    [Fact]
    public void Copy_RefusesAnOccupiedDestinationDataFolder()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        Directory.CreateDirectory(dir.Combine("Bar"));
        File.WriteAllText(dir.Combine("Bar", "mine.txt"), "precious");

        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("Bar.ream")));

        Assert.False(File.Exists(dir.Combine("Bar.ream")));
        Assert.Equal(["mine.txt"], Directory.GetFileSystemEntries(dir.Combine("Bar")).Select(Path.GetFileName));
    }

    [Fact]
    public void Copy_RefusesTheSourceItself()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        var before = Fingerprint(dir.Path);

        Assert.Throws<IOException>(() => ReamCopier.Copy(source, source));
        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("FOO.ream")));

        Assert.Equal(before, Fingerprint(dir.Path));
    }

    [Fact]
    public void Copy_RefusesADestinationInsideTheSourceDataFolder()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        Directory.CreateDirectory(dir.Combine("Foo", "sub"));
        var before = Fingerprint(dir.Path);

        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("Foo", "Bar.ream")));
        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("Foo", "sub", "Bar.ream")));
        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("Foo", "ws-aaaaaaaa", "Bar.ream")));

        Assert.Equal(before, Fingerprint(dir.Path));
    }

    [Fact]
    public void Copy_SameFolderSource_RefusesAWorkspaceFolderOrAWorkspaceLookingName()
    {
        using var dir = new TempDir();
        Populate(TestReam.Repo(dir.Path));
        string source = dir.Combine("Test.ream");
        var before = Fingerprint(dir.Path);

        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("ws-aaaaaaaa", "Bar.ream")));
        Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("ws-cccccccc.ream")));

        Assert.Equal(before, Fingerprint(dir.Path));
    }

    [Fact]
    public void Copy_MissingSource_Throws()
    {
        using var dir = new TempDir();

        Assert.Throws<FileNotFoundException>(() => ReamCopier.Copy(dir.Combine("Nope.ream"), dir.Combine("Bar.ream")));

        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void Copy_RequiresReamExtensionsAndAnExistingDestinationFolder()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        File.WriteAllText(dir.Combine("Foo.txt"), "{}");

        Assert.Throws<ArgumentException>(() => ReamCopier.Copy(dir.Combine("Foo.txt"), dir.Combine("Bar.ream")));
        Assert.Throws<ArgumentException>(() => ReamCopier.Copy(source, dir.Combine("Bar.txt")));
        Assert.Throws<DirectoryNotFoundException>(() => ReamCopier.Copy(source, dir.Combine("missing", "Bar.ream")));

        Assert.False(Directory.Exists(dir.Combine("missing")));
        Assert.False(Directory.Exists(dir.Combine("Bar")));
    }

    [Fact]
    public void Copy_RefusesANewerSchema()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        File.WriteAllText(source, "{ \"schemaVersion\": 99, \"dataFolder\": \"Foo\", \"workspaces\": [] }");

        Assert.Throws<InvalidDataException>(() => ReamCopier.Copy(source, dir.Combine("Bar.ream")));

        Assert.False(File.Exists(dir.Combine("Bar.ream")));
        Assert.False(Directory.Exists(dir.Combine("Bar")));
    }

    [Fact]
    public void Copy_RefusesANonJsonSource()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        File.WriteAllText(source, "not json at all");

        Assert.Throws<InvalidDataException>(() => ReamCopier.Copy(source, dir.Combine("Bar.ream")));

        Assert.Equal("not json at all", File.ReadAllText(source));
        Assert.False(Directory.Exists(dir.Combine("Bar")));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\\\elsewhere")]
    [InlineData("a/b")]
    [InlineData("C:\\\\Windows")]
    public void Copy_RefusesAnUnsafeDataFolderInTheSource(string dataFolder)
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        File.WriteAllText(source, "{ \"schemaVersion\": 1, \"dataFolder\": \"" + dataFolder + "\", \"workspaces\": [] }");

        Assert.Throws<InvalidDataException>(() => ReamCopier.Copy(source, dir.Combine("Bar.ream")));

        Assert.False(File.Exists(dir.Combine("Bar.ream")));
        Assert.False(Directory.Exists(dir.Combine("Bar")));
    }

    [Fact]
    public void Copy_WhenAFileCannotBeRead_LeavesNothingBehindButWhatWasThere()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Src", "Foo.ream");
        var repo = new DocumentRepository(source);
        Populate(repo);
        string locked = Directory.GetFiles(dir.Combine("Src", "Foo", "ws-bbbbbbbb"), "*.reamnote").Single();

        string outDir = dir.Combine("Out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "keep.txt"), "unrelated");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => ReamCopier.Copy(source, Path.Combine(outDir, "Bar.ream")));
        }

        Assert.False(File.Exists(Path.Combine(outDir, "Bar.ream")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "Bar")));
        Assert.Equal(["keep.txt"], Directory.GetFileSystemEntries(outDir).Select(Path.GetFileName));
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(outDir, "keep.txt")));

        // The failure is not sticky: once the file is free the same copy succeeds.
        ReamCopier.Copy(source, Path.Combine(outDir, "Bar.ream"));
        Assert.Equal(Describe(repo.Load()), Describe(new DocumentRepository(Path.Combine(outDir, "Bar.ream")).Load()));
    }

    [Fact]
    public void Copy_WhenItFails_TheReamFileIsNeverWritten()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        string locked = Directory.GetFiles(dir.Combine("Foo", "ws-aaaaaaaa"), "*.reamnote").First();

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => ReamCopier.Copy(source, dir.Combine("Bar.ream")));
        }

        Assert.Empty(Directory.GetFiles(dir.Path, "Bar.ream*"));
        Assert.False(Directory.Exists(dir.Combine("Bar")));
    }

    [Fact]
    public void Copy_AlreadyCancelled_ThrowsAndCreatesNothing()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Foo.ream");
        Populate(new DocumentRepository(source));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var before = Fingerprint(dir.Path);

        Assert.Throws<OperationCanceledException>(() => ReamCopier.Copy(source, dir.Combine("Bar.ream"), cancelled.Token));

        Assert.Equal(before, Fingerprint(dir.Path));
    }

    [Fact]
    public void Copy_CancelledPartWayThrough_RemovesWhatItMade()
    {
        using var dir = new TempDir();
        string source = dir.Combine("Src", "Foo.ream");
        var repo = new DocumentRepository(source);
        var notes = Enumerable.Range(0, 400).Select(i => Note($"n{i}", new string('x', 500 + i))).ToArray();
        repo.Save(new DocumentSnapshot([Workspace("Big", "ws-aaaaaaaa", notes)], null));

        string outDir = dir.Combine("Out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "keep.txt"), "unrelated");
        var before = Fingerprint(dir.Combine("Src"));

        // Cancel the moment the first copied file shows up in the destination.
        using var cancel = new CancellationTokenSource();
        using var watcher = new FileSystemWatcher(outDir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, e) =>
        {
            try { if (e.Name!.EndsWith(".reamnote", StringComparison.OrdinalIgnoreCase)) cancel.Cancel(); }
            catch (ObjectDisposedException) { }
        };
        watcher.EnableRaisingEvents = true;

        Assert.Throws<OperationCanceledException>(() => ReamCopier.Copy(source, Path.Combine(outDir, "Bar.ream"), cancel.Token));

        Assert.Equal(["keep.txt"], Directory.GetFileSystemEntries(outDir).Select(Path.GetFileName));
        Assert.Equal(before, Fingerprint(dir.Combine("Src")));
    }
}
