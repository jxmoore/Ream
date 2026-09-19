namespace Ream.Core.Layout;

/// <summary>
/// Whether the ribbon panel is up or tucked away, as pure state (the window supplies the pointer and the timer).
/// With auto-hide on, the panel shows while the pointer is over the tab row or the panel, while one of its menus or
/// drop-downs is open, after a tab was clicked (until you click elsewhere or press Escape), or while it is pinned;
/// with auto-hide off it is always there.
/// </summary>
public sealed class RibbonVisibility
{
    private bool _autoHide = true;

    /// <summary>Turning it off also drops the pin and the tab click: a docked ribbon has nothing to hold open.</summary>
    public bool AutoHide
    {
        get => _autoHide;
        set
        {
            _autoHide = value;
            if (!value)
            {
                Pinned = false;
                Engaged = false;
            }
        }
    }

    /// <summary>Kept open on purpose (the pin button), until it is toggled off. The window docks a pinned ribbon.</summary>
    public bool Pinned { get; private set; }

    /// <summary>Opened by clicking a tab: stays up, pointer or no pointer, until <see cref="Dismiss"/>.</summary>
    public bool Engaged { get; private set; }

    /// <summary>The pointer is over the tab row or the panel.</summary>
    public bool PointerInside { get; set; }

    /// <summary>A drop-down or menu that belongs to the ribbon is open; hiding now would pull it away.</summary>
    public bool MenuOpen { get; set; }

    /// <summary>Whether the panel should be up right now, before any hide delay.</summary>
    public bool WantsOpen => !AutoHide || Pinned || Engaged || PointerInside || MenuOpen;

    /// <summary>A tab was clicked: a tab that was not showing engages; clicking the showing tab again toggles it.</summary>
    public void TabClicked(bool wasSelected)
    {
        if (AutoHide) Engaged = !wasSelected || !Engaged;
    }

    /// <summary>The pin button: keep the panel open (docked) until toggled again.</summary>
    public void TogglePin()
    {
        if (AutoHide) Pinned = !Pinned;
    }

    /// <summary>A click elsewhere or Escape: forget the tab click and the pointer. The pin is the user's to undo.</summary>
    public void Dismiss()
    {
        Engaged = false;
        PointerInside = false;
    }
}
