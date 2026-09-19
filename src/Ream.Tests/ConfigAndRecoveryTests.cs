using Ream.Core.Models;
using Ream.Persistence.Io;
using Ream.Persistence.NoteFormat;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class ConfigTryLoadTests
{
    private static string Write(TempDir dir, string json)
    {
        string path = dir.Combine("config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ValidFile_IsReturned_WithMissingKeybindingsFilledIn()
    {
        using var dir = new TempDir();
        var store = new AppConfigStore(Write(dir, """{ "layout": { "gapPx": 8 }, "animations": {}, "keybindings": { "newNote": "Ctrl+T" } }"""));

        Assert.True(store.TryLoad(out var config, out var error));

        Assert.Null(error);
        Assert.Equal(8, config.Layout.GapPx);
        Assert.Equal("Ctrl+T", config.Keybindings["newNote"]);
        Assert.Equal("Alt+Right", config.Keybindings["focusNextNote"]);
    }

    [Fact]
    public void InvalidJson_IsReported_AndTheFileIsLeftExactlyAsTheUserWroteIt()
    {
        using var dir = new TempDir();
        const string halfTyped = """{ "layout": { "gapPx": 8, """;
        string path = Write(dir, halfTyped);

        Assert.False(new AppConfigStore(path).TryLoad(out _, out var error));

        Assert.Contains("not valid JSON", error);
        Assert.Equal(halfTyped, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*"));
    }

    [Fact]
    public void MissingFile_IsReported_NotCreated()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");

        Assert.False(new AppConfigStore(path).TryLoad(out _, out var error));

        Assert.Contains("not found", error);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NullSection_IsReported()
    {
        using var dir = new TempDir();

        Assert.False(new AppConfigStore(Write(dir, """{ "layout": null }""")).TryLoad(out _, out var error));

        Assert.Contains("section", error);
    }

    [Theory]
    [InlineData("""{ "layout": { "gapPx": -1 } }""", "gapPx")]
    [InlineData("""{ "layout": { "gapPx": 500 } }""", "gapPx")]
    [InlineData("""{ "animations": { "resizeMs": -5 } }""", "resizeMs")]
    [InlineData("""{ "animations": { "workspaceSwitchMs": 99999 } }""", "workspaceSwitchMs")]
    [InlineData("""{ "animations": { "columnFocusMs": 6000 } }""", "columnFocusMs")]
    public void OutOfRangeValues_AreRejected_WithTheNameOfTheProblem(string json, string mention)
    {
        using var dir = new TempDir();

        Assert.False(new AppConfigStore(Write(dir, json)).TryLoad(out _, out var error));

        Assert.Contains(mention, error);
    }

    [Fact]
    public void ZeroDurationsAndGap_AreFine()
    {
        using var dir = new TempDir();

        Assert.True(new AppConfigStore(Write(dir, """{ "layout": { "gapPx": 0 }, "animations": { "resizeMs": 0 } }""")).TryLoad(out var config, out _));

        Assert.Equal(0, config.Layout.GapPx);
    }

    [Fact]
    public void StartupLoad_SetsAsideAnOutOfRangeConfig_AndUsesDefaults()
    {
        using var dir = new TempDir();
        string path = Write(dir, """{ "layout": { "gapPx": -50 } }""");

        var config = new AppConfigStore(path).Load("docs");

        Assert.Equal(16, config.Layout.GapPx);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
    }

    [Fact]
    public void Theme_DefaultsToSystem_AndIsHonoredWhenSet()
    {
        using var dir = new TempDir();
        Assert.Equal("system", new AppConfigStore(dir.Combine("fresh", "config.json")).Load("docs").Theme);

        string path = Write(dir, """{ "theme": "dark" }""");
        Assert.True(new AppConfigStore(path).TryLoad(out var config, out _));
        Assert.Equal("dark", config.Theme);
    }

    // ----- Interrupted writes -----

    [Fact]
    public void CompleteTempFile_IsPromoted_WhenTheRealFileIsMissing()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path + ".tmp", """{ "layout": { "gapPx": 5 }, "animations": {}, "keybindings": {} }""");

        var config = new AppConfigStore(path).Load("docs");

        Assert.Equal(5, config.Layout.GapPx);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void TempFile_NextToARealConfig_IsSetAside_AndTheRealOneKept()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, """{ "layout": { "gapPx": 7 }, "animations": {}, "keybindings": {} }""");
        File.WriteAllText(path + ".tmp", "leftover");

        var config = new AppConfigStore(path).Load("docs");

        Assert.Equal(7, config.Layout.GapPx);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.interrupted-*"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void PartialTempFile_IsSetAside_NotPromoted()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path + ".tmp", """{ "layout": { "gapPx": 5""");

        var config = new AppConfigStore(path).Load("docs");

        Assert.Equal(16, config.Layout.GapPx);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.interrupted-*"));
    }
}

public class DocumentRecoveryTests
{
    private static NoteSnapshot Note(string body = "body") => new(Guid.NewGuid(), "n", body, 0.5, false);

    private static string Seed(TempDir dir, out NoteSnapshot note)
    {
        string root = dir.Combine("Docs");
        note = Note();
        new DocumentRepository(root).Save(new DocumentSnapshot(
            [new WorkspaceSnapshot(Guid.NewGuid(), "W", "ws-11111111", [note], note.Id)], null));
        return root;
    }

    [Fact]
    public void CompleteNoteTempFile_IsPromoted_WhenTheNoteFileIsMissing()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out _);
        var orphanId = Guid.NewGuid();
        string folder = Path.Combine(root, "ws-11111111");
        File.WriteAllText(
            Path.Combine(folder, orphanId.ToString("N") + ".reamnote.tmp"),
            """<ReamNote schemaVersion="1"><Doc><P><R>saved just before the crash</R></P></Doc></ReamNote>""");

        var notes = new DocumentRepository(root).Load().Workspaces[0].Notes;

        Assert.Contains(notes, n => n.Id == orphanId && n.Body.Contains("saved just before the crash"));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void TempFile_NextToItsRealFile_IsSetAside_AndTheRealFileUntouched()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out var note);
        string file = Path.Combine(root, "ws-11111111", note.Id.ToString("N") + ".reamnote");
        File.WriteAllText(file + ".tmp", "newer, unverified");

        var loaded = new DocumentRepository(root).Load().Workspaces[0].Notes.Single(n => n.Id == note.Id);

        Assert.Equal("body", loaded.Body);
        Assert.False(File.Exists(file + ".tmp"));
        var recovered = Directory.GetFiles(Path.Combine(root, ".recovered"), "*", SearchOption.AllDirectories).Single();
        Assert.Equal("newer, unverified", File.ReadAllText(recovered));
        Assert.EndsWith(Path.Combine("ws-11111111", note.Id.ToString("N") + ".reamnote.tmp"), recovered);
    }

    [Fact]
    public void PartialJsonTempFile_IsSetAside_NotPromoted()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out _);
        string layout = Path.Combine(root, "ws-11111111", "layout.json");
        File.Delete(layout);
        File.WriteAllText(layout + ".tmp", """{ "notes": [ { "noteId": """);

        new DocumentRepository(root).Load();

        Assert.False(File.Exists(layout + ".tmp"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, ".recovered"), "layout.json.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void PartialNoteTempFile_IsSetAside_NotPromoted()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out _);
        var orphanId = Guid.NewGuid();
        string temp = Path.Combine(root, "ws-11111111", orphanId.ToString("N") + ".reamnote.tmp");
        File.WriteAllText(temp, """<ReamNote schemaVersion="1"><Doc><P><R>cut off mid""");

        var notes = new DocumentRepository(root).Load().Workspaces[0].Notes;

        Assert.DoesNotContain(notes, n => n.Id == orphanId);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, ".recovered"), "*.reamnote.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void ImageTempFile_IsPromotedOnlyIfTheFileIsWhole() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        string root = Seed(dir, out var note);
        string assets = Path.Combine(root, "ws-11111111", "assets", note.Id.ToString("N"));
        Directory.CreateDirectory(assets);

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
        byte[] whole = NoteImage.EncodePng(bitmap);
        File.WriteAllBytes(Path.Combine(assets, "whole.png.tmp"), whole);
        File.WriteAllBytes(Path.Combine(assets, "cut.png.tmp"), whole[..(whole.Length - 20)]);

        new DocumentRepository(root).Load();

        Assert.True(File.Exists(Path.Combine(assets, "whole.png")));
        Assert.False(File.Exists(Path.Combine(assets, "cut.png")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, ".recovered"), "cut.png.tmp", SearchOption.AllDirectories));
    });

    [Fact]
    public void LeftoversInsideTrashAndRecovered_AreNotTouched()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out _);
        Directory.CreateDirectory(Path.Combine(root, ".trash", "ws-x"));
        string trashed = Path.Combine(root, ".trash", "ws-x", "old.reamnote.tmp");
        File.WriteAllText(trashed, "keep me where I am");

        new DocumentRepository(root).Load();

        Assert.True(File.Exists(trashed));
    }

    [Fact]
    public void NoLeftovers_MeansNothingIsMoved()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out _);

        new DocumentRepository(root).Load();

        Assert.False(Directory.Exists(Path.Combine(root, ".recovered")));
    }

    [Fact]
    public void RecoveredFolder_IsNotMistakenForAWorkspace()
    {
        using var dir = new TempDir();
        string root = Seed(dir, out var note);
        File.WriteAllText(Path.Combine(root, "ws-11111111", note.Id.ToString("N") + ".reamnote.tmp"), "x");

        var loaded = new DocumentRepository(root).Load();

        Assert.Single(loaded.Workspaces);
    }
}

public class ConfigWatcherTests
{
    private static bool Wait(ManualResetEventSlim signal) => signal.Wait(TimeSpan.FromSeconds(5));

    [Fact]
    public void WritingTheFile_RaisesAChange()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{}");
        using var signal = new ManualResetEventSlim();
        using var watcher = new ConfigWatcher(path, signal.Set, TimeSpan.FromMilliseconds(50));

        File.WriteAllText(path, """{ "theme": "dark" }""");

        Assert.True(Wait(signal));
    }

    [Fact]
    public void SafeSavesThatRenameATempFileIntoPlace_AreSeen()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{}");
        using var signal = new ManualResetEventSlim();
        using var watcher = new ConfigWatcher(path, signal.Set, TimeSpan.FromMilliseconds(50));

        AtomicFile.WriteAllText(path, """{ "theme": "light" }""");

        Assert.True(Wait(signal));
    }

    [Fact]
    public void CreatingTheFile_IsSeen()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        using var signal = new ManualResetEventSlim();
        using var watcher = new ConfigWatcher(path, signal.Set, TimeSpan.FromMilliseconds(50));

        File.WriteAllText(path, "{}");

        Assert.True(Wait(signal));
    }

    [Fact]
    public void ABurstOfWrites_IsCoalesced()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{}");
        int calls = 0;
        using var watcher = new ConfigWatcher(path, () => Interlocked.Increment(ref calls), TimeSpan.FromMilliseconds(300));

        for (int i = 0; i < 6; i++)
        {
            File.WriteAllText(path, $$"""{ "n": {{i}} }""");
            Thread.Sleep(20);
        }
        Thread.Sleep(1200);

        Assert.InRange(Volatile.Read(ref calls), 1, 2);
    }

    [Fact]
    public void OtherFilesInTheFolder_AreIgnored()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{}");
        int calls = 0;
        using var watcher = new ConfigWatcher(path, () => Interlocked.Increment(ref calls), TimeSpan.FromMilliseconds(50));

        File.WriteAllText(dir.Combine("other.txt"), "noise");
        Thread.Sleep(600);

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void AfterDispose_NothingIsRaised()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{}");
        int calls = 0;
        var watcher = new ConfigWatcher(path, () => Interlocked.Increment(ref calls), TimeSpan.FromMilliseconds(50));
        watcher.Dispose();

        File.WriteAllText(path, """{ "a": 1 }""");
        Thread.Sleep(600);

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void TheCallback_CanBeMarshalledToAnotherThread()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{}");
        using var signal = new ManualResetEventSlim();
        bool marshalled = false;
        using var watcher = new ConfigWatcher(
            path, signal.Set, TimeSpan.FromMilliseconds(50),
            action =>
            {
                marshalled = true;
                action();
            });

        File.WriteAllText(path, """{ "a": 1 }""");

        Assert.True(Wait(signal));
        Assert.True(marshalled);
    }
}

public class ThemeChoiceTests
{
    [Theory]
    [InlineData("light", false, true)]
    [InlineData("light", true, true)]
    [InlineData("dark", true, false)]
    [InlineData("dark", false, false)]
    [InlineData("system", true, true)]
    [InlineData("system", false, false)]
    [InlineData(null, false, false)]
    [InlineData("", true, true)]
    [InlineData("nonsense", false, false)]
    [InlineData("  DARK ", true, false)]
    [InlineData("Light", false, true)]
    public void ResolvesTheSetting_AndFollowsTheSystemOtherwise(string? setting, bool systemIsLight, bool expected)
    {
        Assert.Equal(expected, ThemeChoice.IsLight(setting, systemIsLight));
    }
}
