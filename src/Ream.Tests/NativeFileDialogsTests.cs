using Microsoft.Win32;
using Ream.App.Services;

namespace Ream.Tests;

/// <summary>
/// NativeFileDialogs never actually shows a dialog in a test (there is no way to drive the real OS one): the show
/// delegate is replaced, so these check what was asked for (title, filter, folder, file name) and that the answer
/// it hands back becomes the picked path - or null on a cancel.
/// </summary>
public class NativeFileDialogsTests
{
    private const string Docs = @"C:\Users\Joe\Documents";

    [Fact]
    public void PickOpenReam_AsksForAReamFile_InTheGivenFolder()
    {
        OpenFileDialog? seen = null;
        var dialogs = new NativeFileDialogs(showOpen: d => { seen = d; return false; });

        Assert.Null(dialogs.PickOpenReam(Docs));

        Assert.NotNull(seen);
        Assert.Equal("Open ream", seen!.Title);
        Assert.Equal(Docs, seen.InitialDirectory);
        Assert.Contains("*.ream", seen.Filter);
        Assert.Contains("*.*", seen.Filter);
        Assert.True(seen.CheckFileExists);
        Assert.False(seen.Multiselect);
    }

    [Fact]
    public void PickOpenReam_WhenAccepted_ReturnsTheChosenFile()
    {
        var dialogs = new NativeFileDialogs(showOpen: d =>
        {
            d.FileName = Docs + @"\Notes.ream";
            return true;
        });

        Assert.Equal(Docs + @"\Notes.ream", dialogs.PickOpenReam(Docs));
    }

    [Fact]
    public void PickNewReam_AsksToSave_WithTheSuggestedName_InTheGivenFolder()
    {
        SaveFileDialog? seen = null;
        var dialogs = new NativeFileDialogs(showSave: d => { seen = d; return false; });

        Assert.Null(dialogs.PickNewReam("Untitled", Docs));

        Assert.NotNull(seen);
        Assert.Equal("New ream", seen!.Title);
        Assert.Equal("Untitled", seen.FileName);
        Assert.Equal(Docs, seen.InitialDirectory);
        Assert.Equal("ream", seen.DefaultExt);
    }

    [Fact]
    public void PickSaveAs_AsksToSave_WithTheSuggestedName_InTheGivenFolder()
    {
        SaveFileDialog? seen = null;
        var dialogs = new NativeFileDialogs(showSave: d => { seen = d; return false; });

        Assert.Null(dialogs.PickSaveAs("Copy of Notes", Docs));

        Assert.NotNull(seen);
        Assert.Equal("Save ream as", seen!.Title);
        Assert.Equal("Copy of Notes", seen.FileName);
    }

    [Fact]
    public void TheSaveDialogs_NeverAskWindowsToConfirmAnOverwrite()
    {
        // Ream itself always rejects an occupied name with its own message (ReamManager.FreePath); Windows'
        // own "replace it?" prompt would just be a confusing extra step before that.
        SaveFileDialog? seen = null;
        new NativeFileDialogs(showSave: d => { seen = d; return false; }).PickSaveAs("x", Docs);

        Assert.False(seen!.OverwritePrompt);
    }

    [Fact]
    public void PickSave_WhenAccepted_ReturnsTheChosenPath()
    {
        var dialogs = new NativeFileDialogs(showSave: d =>
        {
            d.FileName = Docs + @"\Fresh.ream";
            return true;
        });

        Assert.Equal(Docs + @"\Fresh.ream", dialogs.PickNewReam("Untitled", Docs));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void ACancelledDialog_GivesNoPath(bool? answer)
    {
        var openDialogs = new NativeFileDialogs(showOpen: _ => answer);
        var saveDialogs = new NativeFileDialogs(showSave: _ => answer);

        Assert.Null(openDialogs.PickOpenReam(Docs));
        Assert.Null(saveDialogs.PickNewReam("x", Docs));
        Assert.Null(saveDialogs.PickSaveAs("x", Docs));
    }

    [Fact]
    public void BothShowDelegates_DefaultToNull_SoTheServiceContainerCanConstructItWithNoRegistrationForThem()
    {
        var ctor = Assert.Single(typeof(NativeFileDialogs).GetConstructors());
        Assert.All(ctor.GetParameters(), p => Assert.True(p.IsOptional));
    }
}
