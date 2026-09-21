namespace Ream.Core.Layout;

/// <summary>
/// Where "focus" is in the Help window: one of its sections, or, past the last section, the Close button. Moving works like
/// switching workspaces: down goes to the next, up goes back, and neither goes past the ends.
/// </summary>
public sealed class HelpNavigator
{
    public HelpNavigator(int sectionCount) => SectionCount = Math.Max(0, sectionCount);

    public int SectionCount { get; }

    /// <summary>0 to <see cref="SectionCount"/> - 1 is a section; <see cref="SectionCount"/> is the Close button.</summary>
    public int Index { get; private set; }

    public bool OnClose => Index == SectionCount;

    /// <summary>The highlighted section, or null while Close is.</summary>
    public int? SectionIndex => OnClose ? null : Index;

    /// <summary>Moves to the next section, then to Close. False when already on Close.</summary>
    public bool Down()
    {
        if (Index >= SectionCount) return false;
        Index++;
        return true;
    }

    /// <summary>Moves back to the previous section (from Close, to the last section). False when already on the first.</summary>
    public bool Up()
    {
        if (Index <= 0) return false;
        Index--;
        return true;
    }
}
