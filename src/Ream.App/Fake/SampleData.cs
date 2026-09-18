using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.App.Fake;

/// <summary>In-memory stand-in for real documents until persistence lands (M2).</summary>
internal static class SampleData
{
    public static AppViewModel Create(AppConfig config)
    {
        var personal = Workspace("Personal",
            Note("Groceries", WidthPreset.OneThird, "#f2a65a"),
            Note("Trip ideas", WidthPreset.Half, "#7c9cff"),
            Note("Reading list", WidthPreset.OneThird, "#8bd3a8"));

        var work = Workspace("Work",
            Note("Standup", WidthPreset.OneThird, "#e57a9a"),
            Note("Design notes", WidthPreset.TwoThirds, "#b48cf2"),
            Note("Meeting - Q3 plan", WidthPreset.Half, "#7c9cff"),
            Note("Bugs", WidthPreset.OneThird, "#f2a65a"),
            Note("Scratch", WidthPreset.Half, "#8bd3a8"));

        var unnamed = Workspace(null,
            Note("Loose thoughts", WidthPreset.Full, "#e57a9a"));

        return new AppViewModel(config, [personal, work, unnamed]);
    }

    private static WorkspaceViewModel Workspace(string? name, params NoteViewModel[] notes)
    {
        var workspace = new WorkspaceViewModel(name);
        foreach (var note in notes)
            workspace.InsertAfterFocus(note);
        workspace.SetFocus(0);
        return workspace;
    }

    private static NoteViewModel Note(string title, WidthPreset width, string accent) => new()
    {
        Title = title,
        WidthPreset = width,
        AccentColor = accent,
        Body = string.Join("\n\n", Enumerable.Range(1, 30).Select(i =>
            $"{title} - paragraph {i}. The quick brown fox jumps over the lazy dog while the " +
            "scroll wheel moves this note's own content, not the workspace.")),
    };
}
