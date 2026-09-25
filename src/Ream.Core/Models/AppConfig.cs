namespace Ream.Core.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Legacy. The folder that held all workspaces before reams were single files; it is only read, once, to find
    /// an old folder to convert. A fresh config no longer writes it and nothing new is stored there.
    /// </summary>
    public string? DocumentsRoot { get; init; }

    /// <summary>Whether changes are written to the open ream as you go. Off means only an explicit save writes.</summary>
    public bool AutoSave { get; init; } = true;

    /// <summary>Whether a newly created ream starts with the tutorial. Off starts it empty.</summary>
    public bool TutorialOnNew { get; init; } = true;

    /// <summary>Full path of the .ream file that was open last; Ream reopens it at launch. Null means none.</summary>
    public string? LastReam { get; init; }

    /// <summary>A theme id from <see cref="ThemeCatalog"/>; anything else is the dark default.</summary>
    public string Theme { get; init; } = ThemeCatalog.DefaultId;

    /// <summary>How opaque the canvas behind the notes is, as a percentage (100 = solid). Works with or without <see cref="CanvasBlur"/>.</summary>
    public int CanvasOpacity { get; init; } = 100;

    /// <summary>How opaque each note's background is, as a percentage (100 = solid). The text stays solid at any value.</summary>
    public int NoteOpacity { get; init; } = 100;

    /// <summary>How large a note's content looks, as a percentage (100 = actual size). Scales everything in the editor, not just the font.</summary>
    public int Zoom { get; init; } = 100;

    /// <summary>Full paths of recently opened/saved reams, most recent first, for the File tab's Recent list. Capped at <see cref="RecentReamsLimit"/>.</summary>
    public IReadOnlyList<string> RecentReams { get; init; } = [];

    public const int RecentReamsLimit = 8;

    /// <summary>Blurs what shows through when the canvas is see-through. Off shows the desktop crisp. Turn off if it misbehaves on your GPU.</summary>
    public bool CanvasBlur { get; init; } = true;

    public LayoutConfig Layout { get; init; } = new();
    public RibbonConfig Ribbon { get; init; } = new();
    public AnimationConfig Animations { get; init; } = new();
    public Dictionary<string, string> Keybindings { get; init; } = DefaultKeybindings();

    /// <summary>A copy with the settings the Settings panel edits changed; everything else is carried over.</summary>
    public AppConfig With(string? theme = null, int? canvasOpacity = null, int? noteOpacity = null, bool? autoSave = null, int? zoom = null) => new()
    {
        SchemaVersion = SchemaVersion,
        DocumentsRoot = DocumentsRoot,
        AutoSave = autoSave ?? AutoSave,
        TutorialOnNew = TutorialOnNew,
        LastReam = LastReam,
        Theme = theme ?? Theme,
        CanvasOpacity = canvasOpacity ?? CanvasOpacity,
        NoteOpacity = noteOpacity ?? NoteOpacity,
        Zoom = zoom ?? Zoom,
        RecentReams = RecentReams,
        CanvasBlur = CanvasBlur,
        Layout = Layout,
        Ribbon = Ribbon,
        Animations = Animations,
        Keybindings = Keybindings,
    };

    /// <summary>A copy remembering <paramref name="path"/> as the ream to reopen at launch (null forgets it); everything else is carried over.</summary>
    public AppConfig WithLastReam(string? path) => new()
    {
        SchemaVersion = SchemaVersion,
        DocumentsRoot = DocumentsRoot,
        AutoSave = AutoSave,
        TutorialOnNew = TutorialOnNew,
        LastReam = path,
        Theme = Theme,
        CanvasOpacity = CanvasOpacity,
        NoteOpacity = NoteOpacity,
        Zoom = Zoom,
        RecentReams = RecentReams,
        CanvasBlur = CanvasBlur,
        Layout = Layout,
        Ribbon = Ribbon,
        Animations = Animations,
        Keybindings = Keybindings,
    };

    /// <summary>A copy with the Recent list (File tab) replaced; everything else is carried over.</summary>
    public AppConfig WithRecentReams(IReadOnlyList<string> recentReams) => new()
    {
        SchemaVersion = SchemaVersion,
        DocumentsRoot = DocumentsRoot,
        AutoSave = AutoSave,
        TutorialOnNew = TutorialOnNew,
        LastReam = LastReam,
        Theme = Theme,
        CanvasOpacity = CanvasOpacity,
        NoteOpacity = NoteOpacity,
        Zoom = Zoom,
        RecentReams = recentReams,
        CanvasBlur = CanvasBlur,
        Layout = Layout,
        Ribbon = Ribbon,
        Animations = Animations,
        Keybindings = Keybindings,
    };

    /// <summary>A copy with a Layout field changed (only <paramref name="gapPx"/> and <paramref name="centerFocusedColumn"/> are settable from the View tab); everything else is carried over.</summary>
    public AppConfig WithLayout(double? gapPx = null, bool? centerFocusedColumn = null) => new()
    {
        SchemaVersion = SchemaVersion,
        DocumentsRoot = DocumentsRoot,
        AutoSave = AutoSave,
        TutorialOnNew = TutorialOnNew,
        LastReam = LastReam,
        Theme = Theme,
        CanvasOpacity = CanvasOpacity,
        NoteOpacity = NoteOpacity,
        Zoom = Zoom,
        RecentReams = RecentReams,
        CanvasBlur = CanvasBlur,
        Layout = new LayoutConfig
        {
            GapPx = gapPx ?? Layout.GapPx,
            CenterFocusedColumn = centerFocusedColumn ?? Layout.CenterFocusedColumn,
            FocusFirstNoteOnSwitch = Layout.FocusFirstNoteOnSwitch,
            FocusBorderColor = Layout.FocusBorderColor,
        },
        Ribbon = Ribbon,
        Animations = Animations,
        Keybindings = Keybindings,
    };

    public static Dictionary<string, string> DefaultKeybindings() => new()
    {
        ["focusPrevNote"] = "Alt+Left",
        ["focusNextNote"] = "Alt+Right",
        ["switchWorkspaceUp"] = "Alt+Up",
        ["switchWorkspaceDown"] = "Alt+Down",
        ["moveNoteLeft"] = "Alt+Shift+Left",
        ["moveNoteRight"] = "Alt+Shift+Right",
        ["moveNoteToPrevWorkspace"] = "Alt+Shift+Up",
        ["moveNoteToNextWorkspace"] = "Alt+Shift+Down",
        ["cycleWidthPreset"] = "Alt+R",
        ["resetNoteSize"] = "Alt+Shift+R",
        ["resetWorkspaceSizes"] = "Ctrl+Alt+R",
        ["resetAllSizes"] = "Ctrl+Alt+Shift+R",
        ["sizeUp"] = "Alt+OemPlus",
        ["sizeDown"] = "Alt+OemMinus",
        ["toggleFullscreen"] = "Alt+F11",
        ["toggleAppFullscreen"] = "F11",
        ["newNote"] = "Alt+N",
        ["closeNote"] = "Alt+Q",
        ["renameNote"] = "F2",
        ["renameWorkspace"] = "Shift+F2",
        ["newReam"] = "Ctrl+Shift+N",
        ["openReam"] = "Ctrl+O",
        ["save"] = "Ctrl+S",
        ["saveAs"] = "Ctrl+Shift+S",
        ["clearReam"] = "Alt+Shift+Q",
        ["find"] = "Ctrl+F",
        ["replace"] = "Ctrl+H",
    };
}

public sealed class LayoutConfig
{
    /// <summary>Space between notes, and around the row, in pixels.</summary>
    public double GapPx { get; init; } = 28;

    public bool CenterFocusedColumn { get; init; } = true;

    /// <summary>Moving to another workspace (keys, Alt+wheel, the workspace menu) lands on its first note.</summary>
    public bool FocusFirstNoteOnSwitch { get; init; } = true;
    /// <summary>Color of the border around the focused note, "#RRGGBB" or "#AARRGGBB". Null means the theme's accent.</summary>
    public string? FocusBorderColor { get; init; }
}

public sealed class RibbonConfig
{
    /// <summary>
    /// The tab row is always there; the panel under it tucks away until the pointer visits the tabs or a tab is
    /// clicked (which pins it open). Off keeps the panel docked above the notes.
    /// </summary>
    public bool AutoHide { get; init; } = true;
}

public sealed class AnimationConfig
{
    public bool Enabled { get; init; } = true;
    public int WorkspaceSwitchMs { get; init; } = 250;
    public int ColumnFocusMs { get; init; } = 200;
    public int ResizeMs { get; init; } = 150;
    public string Easing { get; init; } = "easeOutCubic";
}
