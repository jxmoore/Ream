namespace Ream.Core.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Folder holding all workspaces. Null/empty means the default location.</summary>
    public string? DocumentsRoot { get; init; }

    /// <summary>A theme id from <see cref="ThemeCatalog"/>; anything else is the dark default.</summary>
    public string Theme { get; init; } = ThemeCatalog.DefaultId;

    /// <summary>How opaque the canvas behind the notes is, as a percentage (100 = solid). Works with or without <see cref="CanvasBlur"/>.</summary>
    public int CanvasOpacity { get; init; } = 100;

    /// <summary>Blurs what shows through when the canvas is see-through. Off shows the desktop crisp. Turn off if it misbehaves on your GPU.</summary>
    public bool CanvasBlur { get; init; } = true;

    public LayoutConfig Layout { get; init; } = new();
    public AnimationConfig Animations { get; init; } = new();
    public Dictionary<string, string> Keybindings { get; init; } = DefaultKeybindings();

    /// <summary>A copy with the settings the Settings panel edits changed; everything else is carried over.</summary>
    public AppConfig With(string? theme = null, int? canvasOpacity = null) => new()
    {
        SchemaVersion = SchemaVersion,
        DocumentsRoot = DocumentsRoot,
        Theme = theme ?? Theme,
        CanvasOpacity = canvasOpacity ?? CanvasOpacity,
        CanvasBlur = CanvasBlur,
        Layout = Layout,
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
        ["toggleFullscreen"] = "Shift+F11",
        ["toggleAppFullscreen"] = "F11",
        ["newNote"] = "Alt+N",
        ["closeNote"] = "Alt+Q",
        ["renameNote"] = "F2",
        ["renameWorkspace"] = "Shift+F2",
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

public sealed class AnimationConfig
{
    public bool Enabled { get; init; } = true;
    public int WorkspaceSwitchMs { get; init; } = 250;
    public int ColumnFocusMs { get; init; } = 200;
    public int ResizeMs { get; init; } = 150;
    public string Easing { get; init; } = "easeOutCubic";
}
