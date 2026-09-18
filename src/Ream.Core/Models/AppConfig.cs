namespace Ream.Core.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; init; } = 1;
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
        ["toggleFullscreen"] = "Alt+F",
        ["newNote"] = "Alt+N",
        ["closeNote"] = "Alt+Q",
        ["renameWorkspace"] = "Alt+Shift+R",
    };
}

public sealed class LayoutConfig
{
    public double GapPx { get; init; } = 16;
    public bool CenterFocusedColumn { get; init; }
}

public sealed class AnimationConfig
{
    public bool Enabled { get; init; } = true;
    public int WorkspaceSwitchMs { get; init; } = 250;
    public int ColumnFocusMs { get; init; } = 200;
    public int ResizeMs { get; init; } = 150;
    public string Easing { get; init; } = "easeOutCubic";
}
