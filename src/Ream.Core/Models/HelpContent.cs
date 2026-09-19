namespace Ream.Core.Models;

/// <summary>What one configurable action does, in words, and which Help section lists it.</summary>
public sealed record ActionInfo(string Id, string Description, string Section);

public sealed record HelpEntry(string Description, string Gesture);

public sealed record HelpSection(string Title, IReadOnlyList<HelpEntry> Entries);

/// <summary>Plain-English descriptions of every action that can be bound to a key, in the order Help shows them.</summary>
public static class ActionCatalog
{
    public const string Notes = "Notes";
    public const string Workspaces = "Workspaces";
    public const string Layout = "Size and view";

    public static IReadOnlyList<ActionInfo> All { get; } =
    [
        new("newNote", "New note, to the right of this one", Notes),
        new("closeNote", "Close the note", Notes),
        new("renameNote", "Rename the note", Notes),
        new("focusPrevNote", "Previous note (past the first: a new note)", Notes),
        new("focusNextNote", "Next note (past the last: a new note)", Notes),
        new("moveNoteLeft", "Move the note left", Notes),
        new("moveNoteRight", "Move the note right", Notes),

        new("switchWorkspaceUp", "Workspace above", Workspaces),
        new("switchWorkspaceDown", "Workspace below", Workspaces),
        new("moveNoteToPrevWorkspace", "Move the note to the workspace above", Workspaces),
        new("moveNoteToNextWorkspace", "Move the note to the workspace below", Workspaces),
        new("renameWorkspace", "Rename the workspace", Workspaces),

        new("sizeUp", "Make the note wider", Layout),
        new("sizeDown", "Make the note narrower", Layout),
        new("cycleWidthPreset", "Cycle the note's width: 1/3, 1/2, 2/3, full", Layout),
        new("resetNoteSize", "Reset this note's width to the default", Layout),
        new("resetWorkspaceSizes", "Reset every note's width in this workspace", Layout),
        new("resetAllSizes", "Reset every note's width in every workspace", Layout),
        new("toggleFullscreen", "Note fullscreen, and back", Layout),
        new("toggleAppFullscreen", "App fullscreen, and back", Layout),
    ];

    public static ActionInfo? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}

/// <summary>Turns the gesture strings stored in config.json into what a person would read on a key.</summary>
public static class GestureText
{
    public static string Pretty(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "Unbound";

        return string.Join(" + ", gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Part));
    }

    private static string Part(string part) => part switch
    {
        "OemPlus" => "=",
        "OemMinus" => "-",
        "OemComma" => ",",
        "OemPeriod" => ".",
        "Oem2" or "OemQuestion" => "/",
        "Oem5" or "OemPipe" => "\\",
        "OemOpenBrackets" => "[",
        "Oem6" or "OemCloseBrackets" => "]",
        "Oem1" or "OemSemicolon" => ";",
        "Oem7" or "OemQuotes" => "'",
        "Oem3" or "OemTilde" => "`",
        "Return" => "Enter",
        "Next" => "Page Down",
        "Prior" => "Page Up",
        "Back" => "Backspace",
        "Escape" => "Esc",
        "Space" => "Space",
        _ => part,
    };
}

/// <summary>The content of the Help window: every bound action with its current gesture, plus the mouse and editing shortcuts.</summary>
public static class HelpContent
{
    public static IReadOnlyList<HelpSection> Build(IReadOnlyDictionary<string, string> keybindings)
    {
        var sections = new List<HelpSection>();

        foreach (var group in ActionCatalog.All.GroupBy(a => a.Section))
        {
            sections.Add(new HelpSection(
                group.Key,
                group.Select(a => new HelpEntry(
                    a.Description,
                    GestureText.Pretty(keybindings.TryGetValue(a.Id, out var gesture) ? gesture : null))).ToList()));
        }

        // Anything in the config that has no description yet is still listed, so nothing bound is hidden.
        var known = ActionCatalog.All.Select(a => a.Id).ToHashSet();
        var others = keybindings.Where(kv => !known.Contains(kv.Key)).OrderBy(kv => kv.Key).ToList();
        if (others.Count > 0)
            sections.Add(new HelpSection("Other", others.Select(kv => new HelpEntry(kv.Key, GestureText.Pretty(kv.Value))).ToList()));

        sections.Add(new HelpSection("Mouse", Mouse));
        sections.Add(new HelpSection("Editing", Editing));
        return sections;
    }

    private static readonly HelpEntry[] Mouse =
    [
        new("Scroll the note under the pointer", "Scroll"),
        new("Switch workspace", "Alt + Scroll"),
        new("Move along the row of notes", "Shift + Scroll"),
        new("Move along the row of notes", "Tilt wheel"),
        new("Rename a note", "Click its title"),
        new("Resize a note", "Drag its right edge"),
        new("Rename a workspace", "Double-click its name in the title bar"),
    ];

    private static readonly HelpEntry[] Editing =
    [
        new("Bold", "Ctrl + B"),
        new("Italic", "Ctrl + I"),
        new("Underline", "Ctrl + U"),
        new("Align left, center, right, justify", "Ctrl + L / E / R / J"),
        new("Undo and redo", "Ctrl + Z / Y"),
        new("Cut, copy, paste (images too)", "Ctrl + X / C / V"),
        new("Select all", "Ctrl + A"),
    ];
}
