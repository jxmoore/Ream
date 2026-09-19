namespace Ream.Core.Layout;

/// <summary>
/// Whether the ribbon panel is up or tucked away, as pure state (the window supplies the pointer and the timer).
/// With auto-hide on, the panel shows while the pointer is over the tab row or the panel, while one of its menus or
/// drop-downs is open, or while it is pinned; with auto-hide off it is always there.
/// </summary>
public sealed class RibbonVisibility
{
    private bool _autoHide = true;

    /// <summary>Turning it off also drops the pin: a docked ribbon has nothing to pin.</summary>
    public bool AutoHide
    {
        get => _autoHide;
        set
        {
            _autoHide = value;
            if (!value) Pinned = false;
        }
    }

    /// <summary>Kept open by clicking a tab, until the same tab is clicked again.</summary>
    public bool Pinned { get; private set; }

    /// <summary>The pointer is over the tab row or the panel.</summary>
    public bool PointerInside { get; set; }

    /// <summary>A drop-down or menu that belongs to the ribbon is open; hiding now would pull it away.</summary>
    public bool MenuOpen { get; set; }

    /// <summary>Whether the panel should be up right now, before any hide delay.</summary>
    public bool WantsOpen => !AutoHide || Pinned || PointerInside || MenuOpen;

    /// <summary>A tab was clicked: a tab that was not showing opens and pins; the showing one toggles the pin.</summary>
    public void TabClicked(bool wasSelected)
    {
        if (AutoHide) Pinned = !wasSelected || !Pinned;
    }
}
