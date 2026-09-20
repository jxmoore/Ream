using System.Windows;

namespace Ream.App.Services;

/// <summary>The bits of a window that going fullscreen changes. A seam so the logic can be tested without a real window.</summary>
internal interface IWindowFrame
{
    WindowStyle Style { get; set; }
    ResizeMode ResizeMode { get; set; }
    WindowState State { get; set; }

    /// <summary>Position and size of the window while it is in its normal (not maximized) state.</summary>
    Rect NormalBounds { get; set; }
}

/// <summary>
/// F11: the whole app fills the screen without a title bar (over the taskbar), and the next press puts the
/// window back exactly as it was - style, maximized or not, and position.
/// </summary>
internal sealed class FullscreenController(IWindowFrame frame)
{
    private readonly record struct Saved(WindowStyle Style, ResizeMode ResizeMode, WindowState State, Rect NormalBounds);

    private Saved? _saved;

    public bool IsFullscreen => _saved is not null;

    public void Toggle()
    {
        if (IsFullscreen) Exit();
        else Enter();
    }

    public void Enter()
    {
        if (IsFullscreen) return;

        _saved = new Saved(frame.Style, frame.ResizeMode, frame.State, frame.NormalBounds);

        // Drop back to normal first: changing the style of a maximized window leaves it sized for the old
        // frame, and maximizing afresh is what makes a borderless window cover the taskbar.
        if (frame.State != WindowState.Normal) frame.State = WindowState.Normal;
        frame.Style = WindowStyle.None;
        frame.ResizeMode = ResizeMode.NoResize;
        frame.State = WindowState.Maximized;
    }

    public void Exit()
    {
        if (_saved is not { } saved) return;
        _saved = null;

        frame.State = WindowState.Normal;
        frame.Style = saved.Style;
        frame.ResizeMode = saved.ResizeMode;
        frame.NormalBounds = saved.NormalBounds;
        if (saved.State != WindowState.Normal) frame.State = saved.State;
    }
}

/// <summary>The real thing: a WPF window.</summary>
internal sealed class WindowFrame(Window window) : IWindowFrame
{
    public WindowStyle Style
    {
        get => window.WindowStyle;
        set => window.WindowStyle = value;
    }

    public ResizeMode ResizeMode
    {
        get => window.ResizeMode;
        set => window.ResizeMode = value;
    }

    public WindowState State
    {
        get => window.WindowState;
        set => window.WindowState = value;
    }

    public Rect NormalBounds
    {
        // RestoreBounds is the normal-state rectangle even while the window is maximized.
        get => window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        set
        {
            window.Left = value.Left;
            window.Top = value.Top;
            window.Width = value.Width;
            window.Height = value.Height;
        }
    }
}
