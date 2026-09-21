using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class AppConfigStoreTests
{
    [Fact]
    public void FirstRun_WritesADiscoverableDefaultConfig()
    {
        using var dir = new TempDir();
        string path = dir.Combine("Ream", "config.json");

        var config = new AppConfigStore(path).Load();

        Assert.True(File.Exists(path));
        string json = File.ReadAllText(path);
        Assert.Contains("\"gapPx\": 28", json);
        Assert.Contains("\"focusNextNote\": \"Alt+Right\"", json);
        Assert.DoesNotContain("documentsRoot", json);
        Assert.Null(config.DocumentsRoot);
        Assert.Equal(AppConfig.DefaultKeybindings().Count, config.Keybindings.Count);
    }

    [Fact]
    public void UserValues_AreHonoredAndMissingKeybindingsKeepTheirDefaults()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, """
            {
              "layout": { "gapPx": 4, "centerFocusedColumn": true },
              "animations": { "enabled": false, "workspaceSwitchMs": 10 },
              "keybindings": { "newNote": "Ctrl+Alt+N" }
            }
            """);

        var config = new AppConfigStore(path).Load();

        Assert.Equal(4, config.Layout.GapPx);
        Assert.True(config.Layout.CenterFocusedColumn);
        Assert.False(config.Animations.Enabled);
        Assert.Equal(10, config.Animations.WorkspaceSwitchMs);
        Assert.Equal(200, config.Animations.ColumnFocusMs);
        Assert.Equal("Ctrl+Alt+N", config.Keybindings["newNote"]);
        Assert.Equal("Alt+Right", config.Keybindings["focusNextNote"]);
        Assert.Null(config.DocumentsRoot);
    }

    [Fact]
    public void InvalidJson_IsSetAsideAndDefaultsAreUsed()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, "{ nope");

        var config = new AppConfigStore(path).Load();

        Assert.Equal(28, config.Layout.GapPx);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
        Assert.Contains("gapPx", File.ReadAllText(path));
    }

    [Fact]
    public void NullSection_IsTreatedAsCorrupt()
    {
        using var dir = new TempDir();
        string path = dir.Combine("config.json");
        File.WriteAllText(path, """{ "layout": null }""");

        var config = new AppConfigStore(path).Load();

        Assert.NotNull(config.Layout);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
    }
}
