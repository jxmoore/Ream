using System.Xml.Linq;

namespace Ream.Core.Models;

/// <summary>The ream a new user starts with: a short tour of Ream written as ordinary notes, plus the empty ream.</summary>
public static class TutorialReam
{
    private const string Unbound = "(unbound)";

    /// <summary>
    /// Three workspaces, eight notes. Every gesture named in the text is read from <paramref name="config"/>,
    /// so the tutorial stays true after a rebind.
    /// </summary>
    public static DocumentSnapshot Create(AppConfig config, string? configFilePath, string? reamFilePath)
    {
        var keys = new Keys(config);
        string configPath = string.IsNullOrWhiteSpace(configFilePath) ? @"%AppData%\Ream\config.json" : configFilePath;

        var workspaces = new List<WorkspaceSnapshot>
        {
            Workspace("Start here",
                Welcome(keys),
                ChangingWorkspaces(keys)),
            Workspace("Working with notes",
                MakingAndClosing(keys),
                MovingAndResizing(keys),
                WritingAndSaving(keys)),
            Workspace("Making Ream yours",
                Settings(configPath),
                Keybindings(keys, configPath),
                StartingYourOwn(keys, config, reamFilePath)),
        };

        return new DocumentSnapshot(workspaces, workspaces[0].Id);
    }

    /// <summary>An empty ream: no workspaces at all (the app puts you on an empty workspace with a draft note).</summary>
    public static DocumentSnapshot Blank() => new([], null);

    private static NoteSpec Welcome(Keys keys) => new("Welcome to Ream", 0.5,
    [
        Para("A ream is a saved file of notes. Inside it, workspaces are stacked from top to bottom, and each workspace holds a row of notes side by side. Everything is saved as plain files, so your notes stay yours."),
        Gap(),
        Heading("Moving between notes"),
        Para("Press ", Bold(keys.Pretty("focusNextNote")), " to go to the next note, and ", Bold(keys.Pretty("focusPrevNote")), " to go back."),
        Para("Try it: press ", Bold(keys.Pretty("focusNextNote")), " now."),
        Gap(),
        Para("Past the last note, a new blank note opens. A blank note you leave is not kept, so you can't clutter your ream by accident."),
        Gap(),
        Para("When you are ready to start your own ream, press ", Bold(keys.Pretty("clearReam")), " to clear these tutorial notes."),
    ]);

    private static NoteSpec ChangingWorkspaces(Keys keys) => new("Changing workspaces", 0.5,
    [
        Para("You are in the workspace called Start here. Its name is in the bottom-right corner. You see one workspace at a time."),
        Gap(),
        Bullets(
            [Bold(keys.Pretty("switchWorkspaceDown")), " goes to the workspace below."],
            [Bold(keys.Pretty("switchWorkspaceUp")), " goes to the workspace above."],
            [Bold("Alt + scroll wheel"), " switches workspace too."]),
        Gap(),
        Para("Go down now to see more: press ", Bold(keys.Pretty("switchWorkspaceDown")), "."),
    ]);

    private static NoteSpec MakingAndClosing(Keys keys) => new("Making and closing notes", 0.5,
    [
        Bullets(
            [Bold(keys.Pretty("newNote")), " opens a new note to the right of this one."],
            [Bold(keys.Pretty("closeNote")), " closes the focused note. It is moved to the ream's .trash folder, not deleted."],
            ["A blank note you leave disappears by itself."],
            [Bold(keys.Pretty("renameNote")), " renames the note (clicking its title works too)."],
            [Bold(keys.Pretty("renameWorkspace")), " renames the workspace. You can also double-click its name in the bottom-right corner."]),
    ]);

    private static NoteSpec MovingAndResizing(Keys keys) => new("Moving and resizing", 0.6,
    [
        Heading("Moving"),
        Bullets(
            [Bold(keys.Pretty("moveNoteLeft")), " / ", Bold(keys.Pretty("moveNoteRight")), " move the note along the row."],
            [Bold(keys.Pretty("moveNoteToPrevWorkspace")), " / ", Bold(keys.Pretty("moveNoteToNextWorkspace")), " move it to the workspace above or below."]),
        Gap(),
        Heading("Resizing"),
        Bullets(
            [Bold(keys.Pretty("cycleWidthPreset")), " cycles the width: 1/3, 1/2, 2/3, full."],
            [Bold(keys.Pretty("sizeUp")), " / ", Bold(keys.Pretty("sizeDown")), " make the note wider or narrower."],
            ["Or drag the right edge of a note."],
            [Bold(keys.Pretty("resetNoteSize")), " resets this note, ", Bold(keys.Pretty("resetWorkspaceSizes")), " this workspace, ", Bold(keys.Pretty("resetAllSizes")), " every note."]),
        Gap(),
        Heading("Fullscreen"),
        Bullets(
            [Bold(keys.Pretty("toggleFullscreen")), " gives this note the whole window, and back."],
            [Bold(keys.Pretty("toggleAppFullscreen")), " makes the whole app fullscreen, and back."]),
    ]);

    private static NoteSpec WritingAndSaving(Keys keys) => new("Writing and saving", 0.6,
    [
        Para("The Home tab at the top has the formatting tools: bold, italic, underline, size, color, highlight, lists and alignment. The usual ", Bold("Ctrl + B"), ", ", Bold("Ctrl + I"), " and ", Bold("Ctrl + U"), " work too."),
        Para("Paste an image straight into a note with ", Bold("Ctrl + V"), "."),
        Para("Move the pointer to the tabs to show the ribbon, and click a tab to keep it open."),
        Gap(),
        Heading("Saving"),
        Para("The File tab has ", Bold("New"), " (", keys.Pretty("newReam"), "), ", Bold("Open"), " (", keys.Pretty("openReam"), "), ", Bold("Save"), " (", keys.Pretty("save"), ") and ", Bold("Save As"), " (", keys.Pretty("saveAs"), ")."),
        Para("With ", Bold("Auto-save"), " on, changes are written as you go. With it off, the title shows a * while there are unsaved changes, and Ream asks before you close."),
        Gap(),
        Para("The ", Bold("View"), " tab changes the theme and how see-through the canvas and the notes are."),
    ]);

    private static NoteSpec Settings(string configPath) => new("Settings", 0.6,
    [
        Para("All settings live in one plain-text file, ", Bold(configPath), ". Open it in any text editor. Changes apply the moment you save it, with no restart."),
        Para("If a value is wrong, Ream shows \"Config problem\" in the tab row and keeps the settings it had."),
        Gap(),
        Bullets(
            [Bold("theme"), ": " + string.Join(", ", ThemeCatalog.All.Select(t => t.Id)) + "."],
            [Bold("canvasOpacity"), " and ", Bold("noteOpacity"), ": how solid the canvas and notes are, 0 to 100 (also on the View tab)."],
            [Bold("layout.gapPx"), ": the space between notes, in pixels."],
            [Bold("layout.focusFirstNoteOnSwitch"), ": land on the first note when you change workspace."],
            [Bold("ribbon.autoHide"), ": tuck the ribbon away until you visit the tabs."],
            [Bold("animations"), ": turn the motion on or off, and set how long it takes."],
            [Bold("autoSave"), ": write changes as you go."],
            [Bold("tutorialOnNew"), ": start new reams with this tutorial."]),
    ]);

    private static NoteSpec Keybindings(Keys keys, string configPath) => new("Keybindings", 0.6,
    [
        Para("Every key lives in the ", Bold("keybindings"), " section of config.json (", configPath, "), one gesture per action, written like ", Bold($"\"newNote\": \"{keys.Raw("newNote")}\""), "."),
        Para("The ", Bold("Help"), " button on the File tab lists every action with the gesture it has right now."),
        Gap(),
        Heading("Changing a key"),
        Para("Edit the text. A gesture is the modifiers Ctrl, Alt and Shift plus a key. For example, the line that clears a ream is ", Bold($"\"clearReam\": \"{keys.Raw("clearReam")}\""), ": change the text on the right to rebind it."),
        Para("A gesture that is invalid or already used falls back to the default."),
    ]);

    private static NoteSpec StartingYourOwn(Keys keys, AppConfig config, string? reamFilePath)
    {
        var paragraphs = new List<Block>
        {
            Para("Press ", Bold(keys.Pretty("clearReam")), " to clear the ream. Ream asks first. Nothing is really deleted: removed notes are kept in the ream's .trash folder, and with auto-save off you can undo by closing without saving."),
            Para(Bold("File > New"), " (", keys.Pretty("newReam"), ") makes another ream."),
            Para("Don't want this tutorial on every new ream? Set ", Bold("\"tutorialOnNew\": false"), " in config.json."),
            Gap(),
        };

        if (!string.IsNullOrWhiteSpace(reamFilePath))
            paragraphs.Add(Para("This ream was created at ", Bold(reamFilePath), "."));
        paragraphs.Add(Para(Bold("Save As"), " (", keys.Pretty("saveAs"), ") makes a copy you can keep as a backup."));

        return new NoteSpec("Starting your own ream", 0.5, paragraphs);
    }

    private static WorkspaceSnapshot Workspace(string name, params NoteSpec[] specs)
    {
        Guid id = Guid.NewGuid();
        var notes = specs.Select(s => s.ToSnapshot()).ToList();
        return new WorkspaceSnapshot(id, name, "ws-" + id.ToString("N")[..8], notes, notes[0].Id);
    }

    private sealed record NoteSpec(string Title, double WidthFraction, IReadOnlyList<Block> Blocks)
    {
        public NoteSnapshot ToSnapshot()
        {
            Guid id = Guid.NewGuid();
            var doc = new XElement(NoteContent.BodyName, new XElement("P", new XAttribute("size", 26), new XAttribute("b", 1), new XElement("R", Title)));
            foreach (var block in Blocks) doc.Add(block.ToXml());

            var root = new XElement(NoteContent.RootName,
                new XAttribute("schemaVersion", NoteContent.SchemaVersion),
                new XAttribute("id", id.ToString("D")),
                doc);
            return new NoteSnapshot(id, Title, root.ToString(SaveOptions.DisableFormatting), WidthFraction, false);
        }
    }

    private readonly record struct Run(string Text, bool Bold)
    {
        public static implicit operator Run(string text) => new(text, false);

        public XElement ToXml()
        {
            var run = new XElement("R", Text);
            if (Bold) run.SetAttributeValue("b", 1);
            return run;
        }
    }

    private abstract record Block
    {
        public abstract XElement ToXml();
    }

    private sealed record Paragraph(IReadOnlyList<Run> Runs, int? Size = null, bool Bold = false) : Block
    {
        public override XElement ToXml()
        {
            var p = new XElement("P", Runs.Select(r => r.ToXml()));
            if (Size is { } size) p.SetAttributeValue("size", size);
            if (Bold) p.SetAttributeValue("b", 1);
            return p;
        }
    }

    private sealed record BlankLine : Block
    {
        public override XElement ToXml() => new("P");
    }

    private sealed record BulletList(IReadOnlyList<IReadOnlyList<Run>> Items) : Block
    {
        public override XElement ToXml() =>
            new("UL", Items.Select(item => new XElement("LI", new Paragraph(item).ToXml())));
    }

    private static Run Bold(string text) => new(text, true);

    private static Block Para(params Run[] runs) => new Paragraph(runs);

    private static Block Heading(string text) => new Paragraph([text], Size: 18, Bold: true);

    private static Block Gap() => new BlankLine();

    private static Block Bullets(params Run[][] items) => new BulletList(items);

    private sealed class Keys(AppConfig config)
    {
        public string Pretty(string action) =>
            config.Keybindings.TryGetValue(action, out var gesture) && !string.IsNullOrWhiteSpace(gesture)
                ? GestureText.Pretty(gesture)
                : Unbound;

        public string Raw(string action) =>
            config.Keybindings.TryGetValue(action, out var gesture) && !string.IsNullOrWhiteSpace(gesture)
                ? gesture
                : AppConfig.DefaultKeybindings().GetValueOrDefault(action, Unbound);
    }
}
