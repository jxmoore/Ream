using System.Text.Json.Nodes;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>The settings that go with real .ream files: autoSave, tutorialOnNew, lastReam (and the retired documentsRoot).</summary>
public class ReamConfigTests
{
    private static string Write(TempDir dir, string json)
    {
        string path = dir.Combine("config.json");
        File.WriteAllText(path, json);
        return path;
    }

    // ----- Defaults and the fresh file -----

    [Fact]
    public void TheReamSettings_HaveSensibleDefaults()
    {
        var config = new AppConfig();

        Assert.True(config.AutoSave);
        Assert.True(config.TutorialOnNew);
        Assert.Null(config.LastReam);
        Assert.Null(config.DocumentsRoot);
    }

    [Fact]
    public void AFreshFile_ShowsAutoSaveAndTutorialOnNew_ButNoDocumentsRootOrLastReam()
    {
        using var dir = new TempDir();
        string path = dir.Combine("fresh", "config.json");

        var config = new AppConfigStore(path).Load();

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True((bool?)root["autoSave"]);
        Assert.True((bool?)root["tutorialOnNew"]);
        Assert.False(root.ContainsKey("documentsRoot"));
        Assert.False(root.ContainsKey("lastReam"));
        Assert.True(config.AutoSave);
        Assert.True(config.TutorialOnNew);
        Assert.Null(config.LastReam);
        Assert.Null(config.DocumentsRoot);
    }

    [Fact]
    public void ASetAsideConfig_IsReplacedByAFreshFileWithoutDocumentsRoot()
    {
        using var dir = new TempDir();
        string path = Write(dir, "{ nope");

        new AppConfigStore(path).Load();

        Assert.Single(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True((bool?)root["autoSave"]);
        Assert.False(root.ContainsKey("documentsRoot"));
    }

    // ----- Reading -----

    [Fact]
    public void TheReamSettings_AreReadFromTheFile()
    {
        using var dir = new TempDir();
        string path = Write(dir, """
            { "autoSave": false, "tutorialOnNew": false, "lastReam": "C:\\Notes\\Work.ream" }
            """);

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out var error), error);

        Assert.False(config.AutoSave);
        Assert.False(config.TutorialOnNew);
        Assert.Equal(@"C:\Notes\Work.ream", config.LastReam);
    }

    [Fact]
    public void LeavingTheReamSettingsOut_KeepsTheirDefaults()
    {
        using var dir = new TempDir();

        Assert.True(new AppConfigStore(Write(dir, """{ "theme": "nord" }""")).TryLoad(out var config, out _));

        Assert.True(config.AutoSave);
        Assert.True(config.TutorialOnNew);
        Assert.Null(config.LastReam);
    }

    [Fact]
    public void AnOldConfigWithDocumentsRoot_StillLoads_AndKeepsIt()
    {
        using var dir = new TempDir();
        string path = Write(dir, """{ "documentsRoot": "D:/old/ReemDocuments", "theme": "nord" }""");

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out var error), error);

        Assert.Equal("D:/old/ReemDocuments", config.DocumentsRoot);
        Assert.Equal("nord", config.Theme);
        Assert.True(config.AutoSave);
        Assert.Null(config.LastReam);
    }

    [Fact]
    public void StartupLoad_OfAnOldConfig_KeepsItsDocumentsRootAndDoesNotRewriteTheFile()
    {
        using var dir = new TempDir();
        string json = """{ "documentsRoot": "D:/old/ReemDocuments" }""";
        string path = Write(dir, json);

        var config = new AppConfigStore(path).Load();

        Assert.Equal("D:/old/ReemDocuments", config.DocumentsRoot);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("""{ "lastReam": "C:\\Notes\\Work.ream" }""")]
    [InlineData("""{ "lastReam": "C:\\Notes\\Work.REAM" }""")]
    [InlineData("""{ "lastReam": "D:/notes/Work.ream" }""")]
    [InlineData("""{ "lastReam": "" }""")]
    [InlineData("""{ "lastReam": null }""")]
    [InlineData("""{ }""")]
    public void AGoodLastReam_AndAnEmptyOne_AreAccepted(string json)
    {
        using var dir = new TempDir();

        Assert.True(new AppConfigStore(Write(dir, json)).TryLoad(out _, out var error), error);
    }

    // ----- Refusals -----

    [Theory]
    [InlineData("""{ "lastReam": "Work.ream" }""")]
    [InlineData("""{ "lastReam": "notes\\Work.ream" }""")]
    [InlineData("""{ "lastReam": "C:\\Notes\\Work.txt" }""")]
    [InlineData("""{ "lastReam": "C:\\Notes\\Work" }""")]
    [InlineData("""{ "lastReam": "C:\\Notes\\Work.ream.bak" }""")]
    [InlineData("""{ "lastReam": "C:\\Notes\\Wo|rk.ream" }""")]
    [InlineData("""{ "lastReam": "C:\\Notes\\Wo\u0001rk.ream" }""")]
    [InlineData("""{ "lastReam": "   " }""")]
    public void ABadLastReam_IsRefusedWithAClearReason_AndTheFileIsLeftAlone(string json)
    {
        using var dir = new TempDir();
        string path = Write(dir, json);

        Assert.False(new AppConfigStore(path).TryLoad(out _, out var error));

        Assert.Contains("lastReam must be the full path of a .ream file", error);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("""{ "autoSave": "yes" }""")]
    [InlineData("""{ "autoSave": 1 }""")]
    [InlineData("""{ "tutorialOnNew": "no" }""")]
    [InlineData("""{ "lastReam": 5 }""")]
    public void AWrongTypedValue_IsRefusedTheWayACanvasBlurOfTheWrongTypeIs(string json)
    {
        using var dir = new TempDir();
        string path = Write(dir, json);
        using var blurDir = new TempDir();
        string blurPath = Write(blurDir, """{ "canvasBlur": "yes" }""");

        Assert.False(new AppConfigStore(path).TryLoad(out _, out var error));
        Assert.False(new AppConfigStore(blurPath).TryLoad(out _, out var blurError));

        Assert.StartsWith("not valid JSON", error);
        Assert.StartsWith("not valid JSON", blurError);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void AWrongTypedAutoSave_LeavesTheFileUntouched()
    {
        using var dir = new TempDir();
        string json = """{ "autoSave": "yes" }""";
        string path = Write(dir, json);

        Assert.False(new AppConfigStore(path).TryLoad(out _, out _));

        Assert.Equal(json, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
    }

    [Fact]
    public void StartupLoad_SetsAsideABadLastReam_AndUsesDefaults()
    {
        using var dir = new TempDir();
        string path = Write(dir, """{ "lastReam": "Work.txt" }""");

        var config = new AppConfigStore(path).Load();

        Assert.Null(config.LastReam);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
    }

    // ----- With / WithLastReam -----

    private static AppConfig Everything()
    {
        var config = new AppConfig
        {
            DocumentsRoot = "D:/notes",
            AutoSave = false,
            TutorialOnNew = false,
            LastReam = @"C:\Notes\Work.ream",
            Theme = "nord",
            CanvasOpacity = 80,
            NoteOpacity = 70,
            CanvasBlur = false,
            Layout = new LayoutConfig { GapPx = 30, FocusBorderColor = "#123456" },
            Ribbon = new RibbonConfig { AutoHide = false },
            Animations = new AnimationConfig { Enabled = false },
        };
        config.Keybindings["newNote"] = "Ctrl+T";
        return config;
    }

    private static void AssertCarriedOver(AppConfig original, AppConfig copy)
    {
        Assert.Equal(original.SchemaVersion, copy.SchemaVersion);
        Assert.Equal("D:/notes", copy.DocumentsRoot);
        Assert.Equal("nord", copy.Theme);
        Assert.Equal(80, copy.CanvasOpacity);
        Assert.Equal(70, copy.NoteOpacity);
        Assert.False(copy.CanvasBlur);
        Assert.Same(original.Layout, copy.Layout);
        Assert.Same(original.Ribbon, copy.Ribbon);
        Assert.Same(original.Animations, copy.Animations);
        Assert.Same(original.Keybindings, copy.Keybindings);
    }

    [Fact]
    public void WithLastReam_ReturnsACopyWithTheNewPath_AndCarriesEverythingElseOver()
    {
        var original = Everything();

        var changed = original.WithLastReam(@"C:\Other\Play.ream");

        Assert.NotSame(original, changed);
        Assert.Equal(@"C:\Other\Play.ream", changed.LastReam);
        Assert.Equal(@"C:\Notes\Work.ream", original.LastReam);
        Assert.False(changed.AutoSave);
        Assert.False(changed.TutorialOnNew);
        AssertCarriedOver(original, changed);
    }

    [Fact]
    public void WithLastReam_Null_ForgetsThePath()
    {
        var original = Everything();

        var cleared = original.WithLastReam(null);

        Assert.Null(cleared.LastReam);
        Assert.False(cleared.AutoSave);
        Assert.False(cleared.TutorialOnNew);
        AssertCarriedOver(original, cleared);
    }

    [Fact]
    public void With_CarriesTheReamSettingsOver_AndCanChangeAutoSave()
    {
        var original = Everything();

        var same = original.With(theme: "gruvbox");
        Assert.False(same.AutoSave);
        Assert.False(same.TutorialOnNew);
        Assert.Equal(@"C:\Notes\Work.ream", same.LastReam);

        var flipped = original.With(autoSave: true);
        Assert.True(flipped.AutoSave);
        Assert.False(flipped.TutorialOnNew);
        Assert.Equal(@"C:\Notes\Work.ream", flipped.LastReam);
        Assert.Equal("nord", flipped.Theme);
        AssertCarriedOver(original, flipped);
        Assert.False(original.AutoSave);
    }

    // ----- Writing them back -----

    [Fact]
    public void Update_CanSetAndClearLastReam_WithoutDisturbingOtherKeys()
    {
        using var dir = new TempDir();
        string path = Write(dir, """
            {
              "theme": "dracula",
              "layout": { "gapPx": 24 },
              "keybindings": { "newNote": "Ctrl+T" },
              "somethingCustom": [1, 2, 3]
            }
            """);
        var store = new AppConfigStore(path);

        Assert.True(store.Update(root => root["lastReam"] = @"C:\Notes\Work.ream", out var error), error);

        var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(@"C:\Notes\Work.ream", (string?)saved["lastReam"]);
        Assert.Equal("dracula", (string?)saved["theme"]);
        Assert.Equal(24, (int?)saved["layout"]!["gapPx"]);
        Assert.Equal("Ctrl+T", (string?)saved["keybindings"]!["newNote"]);
        Assert.Equal(3, saved["somethingCustom"]!.AsArray().Count);
        Assert.True(store.TryLoad(out var config, out error), error);
        Assert.Equal(@"C:\Notes\Work.ream", config.LastReam);

        Assert.True(store.Update(root => root.Remove("lastReam"), out error), error);

        saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.False(saved.ContainsKey("lastReam"));
        Assert.Equal("dracula", (string?)saved["theme"]);
        Assert.Equal(3, saved["somethingCustom"]!.AsArray().Count);
        Assert.True(store.TryLoad(out config, out error), error);
        Assert.Null(config.LastReam);
    }

    [Fact]
    public void Update_CanFlipAutoSave_WithoutDisturbingOtherKeys()
    {
        using var dir = new TempDir();
        string path = Write(dir, """
            { "theme": "nord", "tutorialOnNew": false, "lastReam": "C:\\Notes\\Work.ream", "canvasOpacity": 55 }
            """);
        var store = new AppConfigStore(path);

        Assert.True(store.Update(root => root["autoSave"] = false, out var error), error);

        Assert.True(store.TryLoad(out var config, out error), error);
        Assert.False(config.AutoSave);
        Assert.False(config.TutorialOnNew);
        Assert.Equal(@"C:\Notes\Work.ream", config.LastReam);
        Assert.Equal("nord", config.Theme);
        Assert.Equal(55, config.CanvasOpacity);

        Assert.True(store.Update(root => root["autoSave"] = true, out error), error);

        Assert.True(store.TryLoad(out config, out error), error);
        Assert.True(config.AutoSave);
        Assert.False(config.TutorialOnNew);
        Assert.Equal("nord", config.Theme);
    }

    [Fact]
    public void Update_OfAFreshFile_KeepsItsReamSettings()
    {
        using var dir = new TempDir();
        var store = new AppConfigStore(dir.Combine("config.json"));
        store.Load();

        Assert.True(store.Update(root => root["lastReam"] = @"C:\Notes\Work.ream", out var error), error);

        var saved = JsonNode.Parse(File.ReadAllText(store.Path))!.AsObject();
        Assert.True((bool?)saved["autoSave"]);
        Assert.True((bool?)saved["tutorialOnNew"]);
        Assert.False(saved.ContainsKey("documentsRoot"));
        Assert.Equal(@"C:\Notes\Work.ream", (string?)saved["lastReam"]);
    }

    [Fact]
    public void Update_RefusesAFileWithComments_LeavingLastReamAlone()
    {
        using var dir = new TempDir();
        string original = """
            {
              // where I keep things
              "autoSave": true
            }
            """;
        string path = Write(dir, original);

        Assert.False(new AppConfigStore(path).Update(root => root["lastReam"] = @"C:\Notes\Work.ream", out var error));

        Assert.Contains("not plain JSON", error);
        Assert.Equal(original, File.ReadAllText(path));
    }
}
