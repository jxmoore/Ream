using System.Text.Json.Nodes;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void TheCatalog_HasTheAgreedThemes_DarkFirst()
    {
        Assert.Equal(
            ["dark", "light", "dracula", "catppuccin", "material", "nord", "gruvbox"],
            ThemeCatalog.All.Select(t => t.Id));
        Assert.Equal("dark", ThemeCatalog.DefaultId);
        Assert.Equal("dark", ThemeCatalog.All[0].Id);
    }

    [Fact]
    public void OnlyLightIsLight()
    {
        Assert.Equal(["light"], ThemeCatalog.All.Where(t => t.IsLight).Select(t => t.Id));
    }

    [Theory]
    [InlineData("dark", "dark")]
    [InlineData("LIGHT", "light")]
    [InlineData("  Dracula ", "dracula")]
    [InlineData("catppuccin", "catppuccin")]
    [InlineData("system", "dark")]
    [InlineData("", "dark")]
    [InlineData(null, "dark")]
    [InlineData("solarized", "dark")]
    public void Resolving_IsForgivingAndFallsBackToDark(string? id, string expected)
    {
        Assert.Equal(expected, ThemeCatalog.Resolve(id).Id);
    }

    [Fact]
    public void EveryThemeHasAUniqueIdNameAndPaletteFile()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Name).Distinct().Count());
        Assert.Equal("Dracula.xaml", ThemeCatalog.Resolve("dracula").FileName);
        Assert.Equal("Dark.xaml", ThemeCatalog.Resolve("dark").FileName);
    }
}

public class RgbaTests
{
    [Theory]
    [InlineData("#ff8800", 255, 255, 136, 0)]
    [InlineData("#80ff8800", 128, 255, 136, 0)]
    [InlineData("  #000000 ", 255, 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    [InlineData("#00000000", 0, 0, 0, 0)]
    public void ParsesRrggbbAndAarrggbb(string text, byte a, byte r, byte g, byte b)
    {
        Assert.True(Rgba.TryParse(text, out var color));
        Assert.Equal(new Rgba(a, r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ff8800")]
    [InlineData("#ff88")]
    [InlineData("#ff88000")]
    [InlineData("#gg0000")]
    [InlineData("red")]
    [InlineData("#-f0000")]
    public void RejectsAnythingElse(string? text)
    {
        Assert.False(Rgba.TryParse(text, out _));
    }
}

public class CanvasStyleTests
{
    [Theory]
    [InlineData(100, true, true, 255)]
    [InlineData(0, true, true, 0)]
    [InlineData(50, true, true, 128)]
    [InlineData(40, true, true, 102)]
    [InlineData(40, false, true, 255)]
    [InlineData(40, true, false, 255)]
    [InlineData(-10, true, true, 0)]
    [InlineData(250, true, true, 255)]
    public void TheAlpha_FollowsThePercentage_OnlyWhereBlurWorks(int percent, bool blur, bool supported, int expected)
    {
        Assert.Equal(expected, CanvasStyle.Alpha(percent, blur, supported));
    }

    [Theory]
    [InlineData(99, true, true, true)]
    [InlineData(0, true, true, true)]
    [InlineData(100, true, true, false)]
    [InlineData(50, false, true, false)]
    [InlineData(50, true, false, false)]
    public void SeeThrough_NeedsBlurSupportAndLessThanFull(int percent, bool blur, bool supported, bool expected)
    {
        Assert.Equal(expected, CanvasStyle.IsSeeThrough(percent, blur, supported));
    }
}

public class SettingsConfigTests
{
    private static string Write(TempDir dir, string json)
    {
        string path = dir.Combine("config.json");
        File.WriteAllText(path, json);
        return path;
    }

    // ----- Defaults and validation -----

    [Fact]
    public void TheNewSettings_HaveSensibleDefaults()
    {
        var config = new AppConfig();

        Assert.Equal("dark", config.Theme);
        Assert.Equal(100, config.CanvasOpacity);
        Assert.True(config.CanvasBlur);
        Assert.Null(config.Layout.FocusBorderColor);
    }

    [Fact]
    public void AFreshFile_ShowsTheNewSettings()
    {
        using var dir = new TempDir();
        string path = dir.Combine("fresh", "config.json");
        new AppConfigStore(path).Load("docs");

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.Equal("dark", (string?)root["theme"]);
        Assert.Equal(100, (int?)root["canvasOpacity"]);
        Assert.True((bool?)root["canvasBlur"]);
    }

    [Fact]
    public void TheSettingsAreReadFromTheFile()
    {
        using var dir = new TempDir();
        string path = Write(dir, """
            { "theme": "nord", "canvasOpacity": 65, "canvasBlur": false, "layout": { "focusBorderColor": "#ff8800" } }
            """);

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out _));

        Assert.Equal("nord", config.Theme);
        Assert.Equal(65, config.CanvasOpacity);
        Assert.False(config.CanvasBlur);
        Assert.Equal("#ff8800", config.Layout.FocusBorderColor);
    }

    [Theory]
    [InlineData("""{ "canvasOpacity": -1 }""", "canvasOpacity")]
    [InlineData("""{ "canvasOpacity": 101 }""", "canvasOpacity")]
    [InlineData("""{ "layout": { "focusBorderColor": "orange" } }""", "focusBorderColor")]
    [InlineData("""{ "layout": { "focusBorderColor": "#12345" } }""", "focusBorderColor")]
    public void BadValues_AreRefusedWithAClearReason(string json, string mentions)
    {
        using var dir = new TempDir();
        string path = Write(dir, json);

        Assert.False(new AppConfigStore(path).TryLoad(out _, out var error));

        Assert.Contains(mentions, error);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("""{ "layout": { "focusBorderColor": "" } }""")]
    [InlineData("""{ "layout": { } }""")]
    [InlineData("""{ "theme": "system" }""")]
    [InlineData("""{ "theme": "no-such-theme" }""")]
    public void AnEmptyBorderColor_AndAnUnknownTheme_AreAccepted(string json)
    {
        using var dir = new TempDir();

        Assert.True(new AppConfigStore(Write(dir, json)).TryLoad(out _, out _));
    }

    [Fact]
    public void WithCopiesEverythingButWhatIsChanged()
    {
        var original = new AppConfig
        {
            DocumentsRoot = "D:/notes",
            Theme = "nord",
            CanvasOpacity = 80,
            CanvasBlur = false,
            Layout = new LayoutConfig { GapPx = 30, FocusBorderColor = "#123456" },
            Animations = new AnimationConfig { Enabled = false },
        };
        original.Keybindings["newNote"] = "Ctrl+T";

        var changed = original.With(theme: "gruvbox", canvasOpacity: 20);

        Assert.Equal("gruvbox", changed.Theme);
        Assert.Equal(20, changed.CanvasOpacity);
        Assert.Equal("D:/notes", changed.DocumentsRoot);
        Assert.False(changed.CanvasBlur);
        Assert.Same(original.Layout, changed.Layout);
        Assert.Same(original.Animations, changed.Animations);
        Assert.Equal("Ctrl+T", changed.Keybindings["newNote"]);
        Assert.Equal("nord", original.Theme);

        var same = original.With();
        Assert.Equal("nord", same.Theme);
        Assert.Equal(80, same.CanvasOpacity);
    }

    // ----- Writing settings back -----

    [Fact]
    public void Update_ChangesOnlyTheKeysItIsGiven()
    {
        using var dir = new TempDir();
        string path = Write(dir, """
            {
              "theme": "dark",
              "layout": { "gapPx": 24, "centerFocusedColumn": true },
              "keybindings": { "newNote": "Ctrl+T", "closeNote": "Alt+Q" },
              "somethingCustom": [1, 2, 3]
            }
            """);

        bool ok = new AppConfigStore(path).Update(root =>
        {
            root["theme"] = "dracula";
            root["canvasOpacity"] = 55;
        }, out var error);

        Assert.True(ok, error);
        var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("dracula", (string?)saved["theme"]);
        Assert.Equal(55, (int?)saved["canvasOpacity"]);
        Assert.Equal(24, (int?)saved["layout"]!["gapPx"]);
        Assert.Equal("Ctrl+T", (string?)saved["keybindings"]!["newNote"]);
        Assert.Equal(3, saved["somethingCustom"]!.AsArray().Count);
    }

    [Fact]
    public void AnUpdatedFile_LoadsBackWithTheChanges()
    {
        using var dir = new TempDir();
        var store = new AppConfigStore(dir.Combine("config.json"));
        store.Load("docs");

        Assert.True(store.Update(root => { root["theme"] = "material"; root["canvasOpacity"] = 30; }, out _));

        Assert.True(store.TryLoad(out var config, out var error), error);
        Assert.Equal("material", config.Theme);
        Assert.Equal(30, config.CanvasOpacity);
        Assert.Equal(AppConfig.DefaultKeybindings().Count, config.Keybindings.Count);
    }

    [Fact]
    public void Update_CreatesTheFileWhenThereIsNone()
    {
        using var dir = new TempDir();
        string path = dir.Combine("sub", "config.json");

        Assert.True(new AppConfigStore(path).Update(root => root["theme"] = "nord", out _));

        Assert.True(new AppConfigStore(path).TryLoad(out var config, out _));
        Assert.Equal("nord", config.Theme);
    }

    [Fact]
    public void Update_RefusesToRewriteAFileWithComments_LeavingItUntouched()
    {
        using var dir = new TempDir();
        string original = """
            {
              // my notes about this file
              "theme": "dark"
            }
            """;
        string path = Write(dir, original);

        bool ok = new AppConfigStore(path).Update(root => root["theme"] = "nord", out var error);

        Assert.False(ok);
        Assert.Contains("not plain JSON", error);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Update_RefusesABrokenFile_LeavingItUntouched()
    {
        using var dir = new TempDir();
        string path = Write(dir, "{ \"theme\": ");

        bool ok = new AppConfigStore(path).Update(root => root["theme"] = "nord", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal("{ \"theme\": ", File.ReadAllText(path));
    }

    [Fact]
    public void Update_LeavesNoTempFilesBehind()
    {
        using var dir = new TempDir();
        string path = Write(dir, "{}");

        new AppConfigStore(path).Update(root => root["theme"] = "nord", out _);

        Assert.Equal(["config.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }
}
