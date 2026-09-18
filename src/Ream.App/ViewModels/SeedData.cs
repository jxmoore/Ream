using Ream.Core.Models;

namespace Ream.App.ViewModels;

/// <summary>Content for a brand-new install, so the first launch isn't an empty screen.</summary>
internal static class SeedData
{
    public static AppViewModel CreateWelcome(AppConfig config)
    {
        var welcome = new WorkspaceViewModel("Welcome");
        var id = Guid.NewGuid();
        welcome.LoadNotes(
        [
            new NoteViewModel
            {
                Id = id,
                Title = "Welcome to Ream",
                AccentColor = SnapshotMapper.AccentFor(id),
                WidthPreset = WidthPreset.Half,
                Body = WelcomeText,
            },
        ], id);

        return new AppViewModel(config, [welcome]);
    }

    private const string WelcomeText =
        "Welcome to Ream\n\n" +
        "Workspaces stack vertically; notes sit side by side in a row within each workspace.\n\n" +
        "Alt+N  new note, opens to the right of the focused one\n" +
        "Alt+Q  close the focused note (moved to .trash, not deleted)\n" +
        "Alt+Left / Alt+Right  move focus between notes\n" +
        "Alt+Up / Alt+Down  switch workspace (or Alt+scroll)\n" +
        "Shift+scroll  move focus along the row\n" +
        "Alt+R  cycle the note's width: 1/3, 1/2, 2/3, full\n" +
        "Alt+F  toggle fullscreen for the focused note\n" +
        "Alt+Shift+Left / Right  move the note within the row\n" +
        "Alt+Shift+Up / Down  move the note to the previous or next workspace\n\n" +
        "Plain scroll always scrolls the note under the cursor.\n\n" +
        "Shortcuts, gaps and animation timing can all be changed in config.json.";
}
