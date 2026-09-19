using Ream.Core.Abstractions;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

/// <summary>Content for a brand-new install, so the first launch isn't an empty screen.</summary>
internal static class SeedData
{
    public static AppViewModel CreateWelcome(AppConfig config, IAssetStore assets)
    {
        var welcome = new WorkspaceViewModel("Welcome", assets);
        var id = Guid.NewGuid();
        welcome.LoadNotes(
        [
            new NoteViewModel
            {
                Id = id,
                Title = "Welcome to Ream",
                WidthFraction = 0.5,
                Body = WelcomeNote(id),
            },
        ], id);

        return new AppViewModel(config, [welcome], 0, assets);
    }

    private static string WelcomeNote(Guid id) => $"""
        <ReamNote schemaVersion="1" id="{id:D}">
          <Doc>
            <P size="26" b="1"><R>Welcome to Ream</R></P>
            <P><R>Workspaces stack vertically; notes sit side by side in a row within each workspace.</R></P>
            <P />
            <UL>
              <LI><P><R b="1">Alt+N</R><R> new note, opens to the right of the focused one</R></P></LI>
              <LI><P><R b="1">Alt+Q</R><R> close the focused note (moved to .trash, not deleted)</R></P></LI>
              <LI><P><R b="1">Alt+Left / Alt+Right</R><R> move focus between notes</R></P></LI>
              <LI><P><R b="1">Alt+Up / Alt+Down</R><R> switch workspace (or Alt+scroll)</R></P></LI>
              <LI><P><R b="1">Shift+scroll</R><R> move focus along the row</R></P></LI>
              <LI><P><R b="1">Alt+R</R><R> cycle the note's width: 1/3, 1/2, 2/3, full</R></P></LI>
              <LI><P><R b="1">Shift+F11</R><R> toggle fullscreen for the focused note</R></P></LI>
              <LI><P><R b="1">Alt+Shift+Left / Right</R><R> move the note within the row</R></P></LI>
              <LI><P><R b="1">Alt+Shift+Up / Down</R><R> move the note to the previous or next workspace</R></P></LI>
            </UL>
            <P />
            <P size="18" b="1"><R>Writing</R></P>
            <P><R>Format text with the toolbar or the usual shortcuts: </R><R b="1">bold</R><R>, </R><R i="1">italic</R><R>, </R><R u="1" s="0">underline</R><R>, </R><R color="#e5484d">color</R><R> and </R><R bg="#fff3a3" color="#1a1a1a">highlight</R><R>.</R></P>
            <P><R>Paste an image straight into a note with Ctrl+V.</R></P>
            <P />
            <P><R>Plain scroll always scrolls the note under the cursor.</R></P>
            <P><R>Shortcuts, gaps, animation timing and the light/dark theme can all be changed in config.json, and take effect as soon as you save it.</R></P>
          </Doc>
        </ReamNote>
        """;
}
