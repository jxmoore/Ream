using System.Text.RegularExpressions;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class TutorialReamTests
{
    private const string ConfigPath = @"C:\Users\Someone\AppData\Roaming\Ream\config.json";
    private const string ReamPath = @"C:\Users\Someone\Documents\Notes.ream";

    private static readonly string[] Titles =
    [
        "Welcome to Ream", "Changing workspaces",
        "Making and closing notes", "Moving and resizing", "Writing and saving",
        "Settings", "Keybindings", "Starting your own ream",
    ];

    private static readonly string[] Actions =
    [
        "focusNextNote", "focusPrevNote", "switchWorkspaceDown", "switchWorkspaceUp",
        "newNote", "closeNote", "renameNote", "renameWorkspace",
        "moveNoteLeft", "moveNoteRight", "moveNoteToPrevWorkspace", "moveNoteToNextWorkspace",
        "cycleWidthPreset", "sizeUp", "sizeDown", "resetNoteSize", "resetWorkspaceSizes", "resetAllSizes",
        "toggleFullscreen", "toggleAppFullscreen",
        "newReam", "openReam", "save", "saveAs", "clearReam",
    ];

    public static IEnumerable<object[]> NamedActions() => Actions.Select(a => new object[] { a });

    private static DocumentSnapshot Create(AppConfig? config = null, string? configPath = ConfigPath, string? reamPath = ReamPath) =>
        TutorialReam.Create(config ?? new AppConfig(), configPath, reamPath);

    private static IReadOnlyList<NoteSnapshot> Notes(DocumentSnapshot snapshot) =>
        snapshot.Workspaces.SelectMany(w => w.Notes).ToList();

    private static string Text(NoteSnapshot note) => NoteContent.ToPlainText(note.Body);

    private static string Text(DocumentSnapshot snapshot, string title) =>
        Text(Notes(snapshot).Single(n => n.Title == title));

    private static string AllText(DocumentSnapshot snapshot) =>
        string.Join("\n", Notes(snapshot).Select(Text));

    private static AppConfig WithKeys(Action<Dictionary<string, string>> change)
    {
        var keys = AppConfig.DefaultKeybindings();
        change(keys);
        return new AppConfig { Keybindings = keys };
    }

    // ----- Shape -----

    [Fact]
    public void Create_MakesThreeNamedWorkspacesWithEightNotes()
    {
        var snapshot = Create();

        Assert.Equal(["Start here", "Working with notes", "Making Ream yours"], snapshot.Workspaces.Select(w => w.Name));
        Assert.Equal([2, 3, 3], snapshot.Workspaces.Select(w => w.Notes.Count));
        Assert.Equal(Titles, Notes(snapshot).Select(n => n.Title));
    }

    [Fact]
    public void EveryBody_ParsesAndStartsWithItsTitle()
    {
        foreach (var note in Notes(Create()))
        {
            Assert.True(NoteContent.TryParse(note.Body, out var document), note.Title);
            Assert.Equal(note.Id.ToString("D"), (string?)document.Root!.Attribute("id"));
            Assert.Equal(NoteContent.TryDeriveTitle(Text(note)), note.Title);
            Assert.Contains(note.Title, Text(note));
            Assert.False(NoteContent.IsBlank(note.Body));
        }
    }

    [Fact]
    public void Ids_AreUnique_AndFolderNamesFollowTheWorkspaceId()
    {
        var snapshot = Create();

        var ids = snapshot.Workspaces.Select(w => w.Id).Concat(Notes(snapshot).Select(n => n.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(3, snapshot.Workspaces.Select(w => w.FolderName).Distinct().Count());
        foreach (var workspace in snapshot.Workspaces)
        {
            Assert.Matches("^ws-[0-9a-f]{8}$", workspace.FolderName);
            Assert.Equal("ws-" + workspace.Id.ToString("N")[..8], workspace.FolderName);
        }
    }

    [Fact]
    public void TheFirstNoteIsFocusedInEachWorkspace_AndTheFirstWorkspaceIsCurrent()
    {
        var snapshot = Create();

        Assert.Equal(snapshot.Workspaces[0].Id, snapshot.CurrentWorkspaceId);
        foreach (var workspace in snapshot.Workspaces)
            Assert.Equal(workspace.Notes[0].Id, workspace.FocusedNoteId);
    }

    [Fact]
    public void Notes_AreNormalWidthAndNotFullscreen()
    {
        foreach (var note in Notes(Create()))
        {
            Assert.InRange(note.WidthFraction, 0.5, 0.6);
            Assert.False(note.IsFullscreen);
        }
    }

    [Fact]
    public void Create_MakesFreshIdsEachTime()
    {
        Assert.NotEqual(Create().Workspaces[0].Id, Create().Workspaces[0].Id);
    }

    [Fact]
    public void Blank_IsAnEmptyReam()
    {
        var blank = TutorialReam.Blank();

        Assert.NotNull(blank);
        Assert.Empty(blank.Workspaces);
        Assert.Null(blank.CurrentWorkspaceId);
    }

    // ----- Storage -----

    [Fact]
    public void ItSavesAndLoadsThroughTheRepository_WithIdenticalBodies()
    {
        using var dir = new TempDir();
        var snapshot = Create();
        var repository = new DocumentRepository(dir.Combine("T.ream"));

        repository.Save(snapshot);
        var loaded = new DocumentRepository(dir.Combine("T.ream")).Load();

        Assert.Equal(snapshot.Workspaces.Select(w => w.Name), loaded.Workspaces.Select(w => w.Name));
        Assert.Equal(Notes(snapshot).Select(n => n.Title), Notes(loaded).Select(n => n.Title));
        Assert.Equal(Notes(snapshot).Select(n => n.Body), Notes(loaded).Select(n => n.Body));
        Assert.Equal(snapshot.CurrentWorkspaceId, loaded.CurrentWorkspaceId);
    }

    // ----- Gestures come from the config -----

    [Theory]
    [MemberData(nameof(NamedActions))]
    public void EveryNamedGesture_IsReadFromTheConfig(string action)
    {
        var config = new AppConfig();
        string expected = GestureText.Pretty(config.Keybindings[action]);

        Assert.Contains(expected, AllText(Create(config)));
    }

    [Theory]
    [MemberData(nameof(NamedActions))]
    public void ARebind_ShowsUpInTheText(string action)
    {
        var config = WithKeys(keys => keys[action] = "Ctrl+Alt+F9");

        Assert.Contains("Ctrl + Alt + F9", AllText(Create(config)));
    }

    [Fact]
    public void AfterARebind_TheNewGestureIsNamed_AndTheOldDefaultIsNot()
    {
        var config = WithKeys(keys =>
        {
            keys["focusNextNote"] = "Ctrl+Alt+J";
            keys["clearReam"] = "Ctrl+Alt+K";
        });

        var snapshot = Create(config);
        string welcome = Text(snapshot, "Welcome to Ream");
        string keybindings = Text(snapshot, "Keybindings");
        string starting = Text(snapshot, "Starting your own ream");

        Assert.Contains("Ctrl + Alt + J", welcome);
        Assert.DoesNotContain("Alt + Right", AllText(snapshot));
        Assert.Contains("Ctrl + Alt + K", welcome);
        Assert.Contains("Ctrl + Alt + K", starting);
        Assert.DoesNotContain("Alt + Shift + Q", AllText(snapshot));
        Assert.Contains("\"clearReam\": \"Ctrl+Alt+K\"", keybindings);
        Assert.DoesNotContain("Alt+Shift+Q", keybindings);
    }

    [Fact]
    public void TheClearReamGesture_IsInTheFirstAndTheLastNote()
    {
        var snapshot = Create();
        string gesture = GestureText.Pretty(new AppConfig().Keybindings["clearReam"]);

        Assert.Contains("press " + gesture + " to clear these tutorial notes.", Text(snapshot, "Welcome to Ream"));
        Assert.Contains(gesture, Text(snapshot, "Starting your own ream"));
    }

    [Fact]
    public void TheKeybindingsNote_ShowsTheRealCurrentGestures_AsTheyAreWrittenInConfigJson()
    {
        string text = Text(Create(), "Keybindings");

        Assert.Contains("\"newNote\": \"Alt+N\"", text);
        Assert.Contains("\"clearReam\": \"Alt+Shift+Q\"", text);
    }

    [Fact]
    public void AMissingKeybinding_ShowsUnbound_InsteadOfCrashing()
    {
        var config = WithKeys(keys => keys.Remove("focusNextNote"));

        var snapshot = Create(config);

        Assert.Contains("press (unbound) now", Text(snapshot, "Welcome to Ream"));
        Assert.All(Notes(snapshot), n => Assert.True(NoteContent.TryParse(n.Body, out _)));
    }

    [Fact]
    public void AnEmptyKeybinding_IsShownAsUnbound()
    {
        var config = WithKeys(keys => keys["newNote"] = "");

        Assert.Contains("(unbound) opens a new note", Text(Create(config), "Making and closing notes"));
    }

    // ----- Paths and settings -----

    [Fact]
    public void TheTutorialOnNewBlurb_IsInTheLastNote()
    {
        string text = Text(Create(), "Starting your own ream");

        Assert.Contains("Don't want this tutorial on every new ream?", text);
        Assert.Contains("\"tutorialOnNew\": false", text);
        Assert.Contains("config.json", text);
    }

    [Fact]
    public void TheConfigPath_IsInTheSettingsNote()
    {
        Assert.Contains(ConfigPath, Text(Create(), "Settings"));
    }

    [Fact]
    public void WithoutAConfigPath_TheSettingsNoteSaysWhereItUsuallyIs()
    {
        var snapshot = Create(configPath: null);

        Assert.Contains(@"%AppData%\Ream\config.json", Text(snapshot, "Settings"));
        Assert.Contains(@"%AppData%\Ream\config.json", Text(snapshot, "Keybindings"));
    }

    [Fact]
    public void TheSettingsNote_MentionsEverySettingItExplains()
    {
        string text = Text(Create(), "Settings");

        foreach (var setting in new[]
        {
            "theme", "canvasOpacity", "noteOpacity", "layout.gapPx", "layout.focusFirstNoteOnSwitch",
            "ribbon.autoHide", "animations", "autoSave", "tutorialOnNew", "Config problem",
        })
            Assert.Contains(setting, text);
        foreach (var theme in ThemeCatalog.All)
            Assert.Contains(theme.Id, text);
    }

    [Fact]
    public void TheReamPath_IsInTheLastNote_WhenGiven()
    {
        Assert.Contains("This ream was created at " + ReamPath, Text(Create(), "Starting your own ream"));
    }

    [Fact]
    public void WithoutAReamPath_ThereIsNoCreatedAtSentence()
    {
        var snapshot = Create(reamPath: null);

        Assert.DoesNotContain("created at", AllText(snapshot));
        Assert.Contains("makes a copy you can keep as a backup", Text(snapshot, "Starting your own ream"));
    }

    [Fact]
    public void PathsWithMarkupCharacters_AreEscaped_AndTheBodiesStillParse()
    {
        const string config = @"C:\R&D\<cfg>\config.json";
        const string ream = @"C:\Notes & <More>\a ""b"".ream";

        var snapshot = Create(configPath: config, reamPath: ream);

        foreach (var note in Notes(snapshot))
            Assert.True(NoteContent.TryParse(note.Body, out _), note.Title);
        Assert.Contains("R&amp;D", Notes(snapshot).Single(n => n.Title == "Settings").Body);
        Assert.Contains("&lt;cfg&gt;", Notes(snapshot).Single(n => n.Title == "Settings").Body);
        Assert.Contains(config, Text(snapshot, "Settings"));
        Assert.Contains(ream, Text(snapshot, "Starting your own ream"));
    }

    [Fact]
    public void NoBodyContainsAnUnescapedAmpersandOrRawPlaceholder()
    {
        foreach (var note in Notes(Create()))
            Assert.DoesNotMatch(new Regex(@"&(?!amp;|lt;|gt;|quot;|apos;)"), note.Body);
    }
}
