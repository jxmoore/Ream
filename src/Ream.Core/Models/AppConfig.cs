namespace Ream.Core.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Folder holding all workspaces. Null/empty means the default location.</summary>
    public string? DocumentsRoot { get; init; }

    /// <summary>"system" (follow Windows), "light", or "dark".</summary>
    public string Theme { get; init; } = "system";

    public LayoutConfig Layout { get; init; } = new();
    public AnimationConfig Animations { get; init; } = new();
    public Dictionary<string, string> Keybindings { get; init; } = DefaultKeybindings();

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
        ["sizeUp"] = "Alt+OemPlus",
        ["sizeDown"] = "Alt+OemMinus",
        ["toggleFullscreen"] = "Shift+F11",
        ["toggleAppFullscreen"] = "F11",
        ["newNote"] = "Alt+N",
        ["closeNote"] = "Alt+Q",
        ["renameWorkspace"] = "Shift+F2",
    };
}

public sealed class LayoutConfig
{
    public double GapPx { get; init; } = 16;
    public bool CenterFocusedColumn { get; init; } = true;
}

public sealed class AnimationConfig
{
    public bool Enabled { get; init; } = true;
    public int WorkspaceSwitchMs { get; init; } = 250;
    public int ColumnFocusMs { get; init; } = 200;
    public int ResizeMs { get; init; } = 150;
    public string Easing { get; init; } = "easeOutCubic";
}
