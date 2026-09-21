using System.Windows.Input;
using Ream.App.Input;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class ReamManagerOpenTests
{
    private static string MakeOther(ManagerRig rig, string name = "Other", bool tutorial = true)
    {
        Directory.CreateDirectory(rig.ReamsFolder);
        var config = new AppConfig { TutorialOnNew = tutorial };
        return ReamLauncher.Create(rig.PathOf(name), config, rig.ConfigPath).Repository.ReamPath;
    }

    private static List<NoteSnapshot> NotesOnDisk(string reamPath) =>
        new DocumentRepository(reamPath).Load().Workspaces.SelectMany(w => w.Notes).ToList();

    [Fact]
    public void Open_LoadsTheChosenReamInPlace_StartsAtItsFirstNote_AndRemembersIt()
    {
        using var rig = new ManagerRig();
        string other = MakeOther(rig);
        rig.Dialogs.OpenAnswers.Enqueue(other);

        Assert.True(rig.Manager.OpenReam());

        Assert.Equal("Other", rig.App.ReamName);
        Assert.Equal("Ream - Other", rig.App.WindowTitle);
        Assert.Equal(3, rig.App.Workspaces.Count(w => !w.IsEdge));
        Assert.Equal(0, rig.App.CurrentWorkspace.FocusedIndex);
        Assert.Equal(other, rig.Manager.Current.ReamPath);
        Assert.True(rig.Store.TryLoad(out var saved, out _));
        Assert.Equal(other, saved.LastReam);
    }

    [Fact]
    public void ACancelledDialog_ChangesNothing()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.OpenAnswers.Enqueue(null);

        Assert.False(rig.Manager.OpenReam());

        Assert.Equal("First", rig.App.ReamName);
        Assert.Empty(rig.Prompts.Errors);
    }

    [Fact]
    public void AFileThatIsMissing_OrNotAReam_IsExplained_AndTheOpenReamStays()
    {
        using var rig = new ManagerRig();

        Assert.False(rig.Manager.OpenReam(rig.PathOf("Ghost")));
        Assert.False(rig.Manager.OpenReam(rig.Dir.Combine("notes.txt")));

        Assert.Equal(2, rig.Prompts.Errors.Count);
        Assert.Equal("First", rig.App.ReamName);
    }

    [Fact]
    public void AReamFromANewerRelease_WillNotOpen_LeavesItAlone_AndKeepsTheCurrentOne()
    {
        using var rig = new ManagerRig();
        string future = rig.PathOf("Future");
        Directory.CreateDirectory(rig.ReamsFolder);
        File.WriteAllText(future, """{ "schemaVersion": 99, "workspaces": [] }""");

        Assert.False(rig.Manager.OpenReam(future));

        Assert.Contains("couldn't open", Assert.Single(rig.Prompts.Errors));
        Assert.Equal("First", rig.App.ReamName);
        Assert.Equal("""{ "schemaVersion": 99, "workspaces": [] }""", File.ReadAllText(future));
    }

    [Fact]
    public void OpeningTheReamThatIsAlreadyOpen_DoesNothing()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "unsaved";

        Assert.True(rig.Manager.OpenReam(rig.PathOf("First")));

        Assert.Empty(rig.Prompts.SaveQuestions);
        Assert.Equal("unsaved", rig.FirstNote.Body);
    }

    [Fact]
    public void WithAutoSaveOn_PendingEditsAreWrittenToTheOldReamFirst()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.FirstNote.Body = "typed just now";
        string other = MakeOther(rig);

        Assert.True(rig.Manager.OpenReam(other));

        Assert.Contains(NotesOnDisk(rig.PathOf("First")), n => n.Body == "typed just now");
    }

    [Fact]
    public void WithAutoSaveOff_UnsavedChangesAsk_SaveKeepsThem_CancelStaysAndNothingSwitches()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "unsaved words";
        string other = MakeOther(rig);

        rig.Prompts.SaveAnswer = SaveChoice.Cancel;
        Assert.False(rig.Manager.OpenReam(other));
        Assert.Equal("First", rig.App.ReamName);
        Assert.Equal("unsaved words", rig.FirstNote.Body);

        rig.Prompts.SaveAnswer = SaveChoice.Save;
        Assert.True(rig.Manager.OpenReam(other));
        Assert.Equal("Other", rig.App.ReamName);
        Assert.Contains(NotesOnDisk(rig.PathOf("First")), n => n.Body == "unsaved words");
    }

    [Fact]
    public void AfterOpen_EditsGoToTheOpenedReamOnly()
    {
        using var rig = new ManagerRig(autoSave: false);
        string other = MakeOther(rig);
        byte[] firstBefore = File.ReadAllBytes(rig.PathOf("First"));
        Assert.True(rig.Manager.OpenReam(other));

        rig.FirstNote.Body = "in the other one";
        Assert.True(rig.Manager.Save());

        Assert.Contains(NotesOnDisk(other), n => n.Body == "in the other one");
        Assert.Equal(firstBefore, File.ReadAllBytes(rig.PathOf("First")));
        Assert.DoesNotContain(NotesOnDisk(rig.PathOf("First")), n => n.Body == "in the other one");
    }

    [Fact]
    public void AnEmptyReam_OpensOnADraftReadyToType()
    {
        using var rig = new ManagerRig();
        string blank = MakeOther(rig, "Blank", tutorial: false);

        Assert.True(rig.Manager.OpenReam(blank));

        Assert.True(Assert.Single(rig.App.CurrentWorkspace.Notes).IsDraft);
        Assert.False(rig.Manager.Current.HasUnsavedChanges);
    }
}

public class ReamManagerSaveTests
{
    private static List<NoteSnapshot> NotesOnDisk(string reamPath) =>
        new DocumentRepository(reamPath).Load().Workspaces.SelectMany(w => w.Notes).ToList();

    [Fact]
    public void Save_WritesWithAutoSaveOff_AndTheStarGoesAway()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "kept";
        rig.Manager.Current.Coordinator.Evaluate();
        Assert.Equal("Ream - First *", rig.App.WindowTitle);

        Assert.True(rig.Manager.Save());

        Assert.Contains(NotesOnDisk(rig.PathOf("First")), n => n.Body == "kept");
        Assert.Equal("Ream - First", rig.App.WindowTitle);
    }

    [Fact]
    public void AFailedSave_IsReported_AndTheChangesStayUnsaved()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "kept";
        rig.Manager.Current.Coordinator.Evaluate();
        string reamFile = rig.PathOf("First");
        File.SetAttributes(reamFile, FileAttributes.ReadOnly);

        try
        {
            Assert.False(rig.Manager.Save());
            Assert.Contains("couldn't save", Assert.Single(rig.Prompts.Errors));
            Assert.True(rig.Manager.Current.HasUnsavedChanges);
        }
        finally
        {
            File.SetAttributes(reamFile, FileAttributes.Normal);
        }
    }

    [Fact]
    public void TurningAutoSaveOff_TakesEffectAtOnce_AndIsWrittenToConfig()
    {
        using var rig = new ManagerRig(autoSave: true);

        rig.Manager.SetAutoSave(false);

        Assert.False(rig.App.AutoSave);
        Assert.False(rig.Manager.Current.AutoSave);
        Assert.False(rig.App.Config.AutoSave);
        Assert.True(rig.Store.TryLoad(out var saved, out _));
        Assert.False(saved.AutoSave);

        rig.FirstNote.Body = "edit";
        rig.Manager.Current.Coordinator.Evaluate();
        Assert.Equal("Ream - First *", rig.App.WindowTitle);

        rig.Manager.SetAutoSave(true);
        Assert.True(rig.App.AutoSave);
        Assert.True(rig.Store.TryLoad(out saved, out _));
        Assert.True(saved.AutoSave);
    }

    [Fact]
    public void ClosingTheWindow_AsksOnlyWhenThereIsSomethingToLose()
    {
        using var rig = new ManagerRig(autoSave: false);
        Assert.True(rig.Manager.ConfirmLeave()); // nothing changed: no question
        Assert.Empty(rig.Prompts.SaveQuestions);

        rig.FirstNote.Body = "unsaved";
        rig.Prompts.SaveAnswer = SaveChoice.Cancel;
        Assert.False(rig.Manager.ConfirmLeave());

        rig.Prompts.SaveAnswer = SaveChoice.DontSave;
        Assert.True(rig.Manager.ConfirmLeave());
        Assert.DoesNotContain(NotesOnDisk(rig.PathOf("First")), n => n.Body == "unsaved");

        rig.Prompts.SaveAnswer = SaveChoice.Save;
        Assert.True(rig.Manager.ConfirmLeave());
        Assert.Contains(NotesOnDisk(rig.PathOf("First")), n => n.Body == "unsaved");
    }

    [Fact]
    public void ClosingWithAutoSaveOn_WritesPendingEditsAndNeverAsks()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.FirstNote.Body = "last words";

        Assert.True(rig.Manager.ConfirmLeave());

        Assert.Empty(rig.Prompts.SaveQuestions);
        Assert.Contains(NotesOnDisk(rig.PathOf("First")), n => n.Body == "last words");
    }
}

public class ReamManagerSaveAsTests
{
    private static List<NoteSnapshot> NotesOnDisk(string reamPath) =>
        new DocumentRepository(reamPath).Load().Workspaces.SelectMany(w => w.Notes).ToList();

    [Fact]
    public void SaveAs_CopiesTheReam_IncludingUnsavedChanges_AndSwitchesToTheCopy()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "unsaved words";
        byte[] originalBefore = File.ReadAllBytes(rig.PathOf("First"));
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("Backup"));

        Assert.True(rig.Manager.SaveAs());

        Assert.Equal("Backup", rig.App.ReamName);
        Assert.Equal(rig.PathOf("Backup"), rig.Manager.Current.ReamPath);
        Assert.Contains(NotesOnDisk(rig.PathOf("Backup")), n => n.Body == "unsaved words");
        Assert.Equal(originalBefore, File.ReadAllBytes(rig.PathOf("First")));
        Assert.DoesNotContain(NotesOnDisk(rig.PathOf("First")), n => n.Body == "unsaved words");
        Assert.Equal(8, NotesOnDisk(rig.PathOf("Backup")).Count);
        Assert.False(rig.Manager.Current.HasUnsavedChanges);
    }

    [Fact]
    public void AfterSaveAs_EditsGoToTheCopyOnly()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("Backup"));
        Assert.True(rig.Manager.SaveAs());
        byte[] originalBefore = File.ReadAllBytes(rig.PathOf("First"));

        rig.FirstNote.Body = "only in the copy";
        Assert.True(rig.Manager.Save());

        Assert.Contains(NotesOnDisk(rig.PathOf("Backup")), n => n.Body == "only in the copy");
        Assert.DoesNotContain(NotesOnDisk(rig.PathOf("First")), n => n.Body == "only in the copy");
        Assert.Equal(originalBefore, File.ReadAllBytes(rig.PathOf("First")));
    }

    [Fact]
    public void ImagesComeAlong()
    {
        using var rig = new ManagerRig();
        var workspace = rig.App.Workspaces.First(w => w.Notes.Count > 0);
        var note = workspace.Notes[0];
        string asset = rig.Manager.Current.Repository.SaveAsset(workspace.FolderName, note.Id, [1, 2, 3, 4]);
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("Backup"));

        Assert.True(rig.Manager.SaveAs());

        var copy = new DocumentRepository(rig.PathOf("Backup"));
        Assert.NotNull(copy.GetAssetPath(workspace.FolderName, note.Id, asset));
    }

    [Fact]
    public void ANameThatIsTaken_IsRefused_NeverOverwritten()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("First"));
        byte[] before = File.ReadAllBytes(rig.PathOf("First"));

        Assert.False(rig.Manager.SaveAs());

        Assert.Contains("already a ream called", Assert.Single(rig.Prompts.Errors));
        Assert.Equal(before, File.ReadAllBytes(rig.PathOf("First")));
        Assert.Equal("First", rig.App.ReamName);
    }

    [Fact]
    public void ACancelledDialog_ChangesNothing_AndTheSuggestionIsACopyName()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.SaveAsAnswers.Enqueue(null);

        Assert.False(rig.Manager.SaveAs());

        Assert.Equal("First", rig.App.ReamName);
        Assert.Equal("First copy.ream", Assert.Single(rig.Dialogs.SuggestedNames));
    }

    [Fact]
    public void YouStayWhereYouWere_OnTheSameWorkspaceAndNote()
    {
        using var rig = new ManagerRig();
        rig.App.SwitchWorkspace(1);
        rig.App.CurrentWorkspace.SetFocus(1);
        string workspaceName = rig.App.CurrentWorkspace.DisplayName!;
        Guid focused = rig.App.CurrentWorkspace.FocusedNote!.Id;
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("Backup"));

        Assert.True(rig.Manager.SaveAs());

        Assert.Equal(workspaceName, rig.App.CurrentWorkspace.DisplayName);
        Assert.Equal(focused, rig.App.CurrentWorkspace.FocusedNote!.Id);
    }

    [Fact]
    public void WithAutoSaveOn_TheOriginalIsBroughtUpToDateToo()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.FirstNote.Body = "typed just now";
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("Backup"));

        Assert.True(rig.Manager.SaveAs());

        Assert.Contains(NotesOnDisk(rig.PathOf("First")), n => n.Body == "typed just now");
        Assert.Contains(NotesOnDisk(rig.PathOf("Backup")), n => n.Body == "typed just now");
    }

    [Fact]
    public void AFailedCopy_LeavesNothingBehind_AndTheOriginalOpen()
    {
        using var rig = new ManagerRig();
        var workspace = rig.App.Workspaces.First(w => w.Notes.Count > 0);
        string noteFile = Path.Combine(rig.Manager.Current.Repository.Root, workspace.FolderName, workspace.Notes[0].Id.ToString("N") + ReamPaths.NoteExtension);
        rig.Dialogs.SaveAsAnswers.Enqueue(rig.PathOf("Backup"));

        using (new FileStream(noteFile, FileMode.Open, FileAccess.Read, FileShare.None)) // holds the note so it cannot be copied
        {
            Assert.False(rig.Manager.SaveAs());
        }

        Assert.Single(rig.Prompts.Errors);
        Assert.False(File.Exists(rig.PathOf("Backup")));
        Assert.False(Directory.Exists(Path.Combine(rig.ReamsFolder, "Backup")));
        Assert.Equal("First", rig.App.ReamName);
    }
}

public class ReamManagerClearTests
{
    private static string[] Trashed(ManagerRig rig) =>
        Directory.Exists(Path.Combine(rig.ReamsFolder, "First", ".trash"))
            ? Directory.GetFiles(Path.Combine(rig.ReamsFolder, "First", ".trash"), "*" + ReamPaths.NoteExtension, SearchOption.AllDirectories)
            : [];

    [Fact]
    public void Clear_AsksFirst_NamingWhatWillGo()
    {
        using var rig = new ManagerRig();
        rig.Prompts.ConfirmAnswer = false;

        Assert.False(rig.Manager.ClearReam());

        Assert.Contains("3 workspaces and 8 notes", Assert.Single(rig.Prompts.Confirmations));
        Assert.Contains(".trash", rig.Prompts.Confirmations[0]);
        Assert.Equal(3, rig.App.Workspaces.Count(w => !w.IsEdge));
    }

    [Fact]
    public void Clear_RemovesEverything_LandsOnADraft_AndNothingIsDeleted()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.Prompts.ConfirmAnswer = true;

        Assert.True(rig.Manager.ClearReam());
        rig.Manager.Current.Coordinator.Flush();

        var draft = Assert.Single(rig.App.CurrentWorkspace.Notes);
        Assert.True(draft.IsDraft);
        Assert.Single(rig.App.Workspaces.Where(w => w.Notes.Count > 0));
        Assert.Equal(8, Trashed(rig).Length);
        Assert.False(Directory.GetDirectories(Path.Combine(rig.ReamsFolder, "First"), "ws-*").Any());
        Assert.True(File.Exists(rig.PathOf("First"))); // the ream itself stays
    }

    [Fact]
    public void AClearedReam_ReopensEmpty_NotAsANewTutorial()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.Prompts.ConfirmAnswer = true;
        rig.Manager.ClearReam();
        rig.Manager.Current.Coordinator.Flush();

        Assert.True(ReamLauncher.TryOpen(rig.PathOf("First"), out var reopened, out _));

        Assert.Empty(reopened!.Snapshot.Workspaces);
    }

    [Fact]
    public void WithAutoSaveOff_ClearIsJustAnUnsavedChange_ClosingWithoutSavingUndoesIt()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.Prompts.ConfirmAnswer = true;

        Assert.True(rig.Manager.ClearReam());
        rig.Manager.Current.Coordinator.Evaluate();
        Assert.True(rig.Manager.Current.HasUnsavedChanges);
        Assert.Equal("Ream - First *", rig.App.WindowTitle);

        rig.Prompts.SaveAnswer = SaveChoice.DontSave;
        Assert.True(rig.Manager.ConfirmLeave());

        Assert.Equal(3, new DocumentRepository(rig.PathOf("First")).Load().Workspaces.Count);
        Assert.Empty(Trashed(rig));
    }

    [Fact]
    public void ABlankDraftIsNotCounted_AndAnEmptyReamHasNothingToClear()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.Prompts.ConfirmAnswer = true;
        rig.Manager.ClearReam();
        rig.Prompts.Confirmations.Clear();

        Assert.True(rig.Manager.ClearReam()); // only a blank draft left

        Assert.Empty(rig.Prompts.Confirmations);
    }

    [Fact]
    public void ASingleWorkspaceAndNote_AreCountedInTheSingular()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.Prompts.ConfirmAnswer = true;
        rig.Manager.ClearReam();
        rig.App.CurrentWorkspace.FocusedNote!.Body = "<ReamNote schemaVersion=\"1\"><Doc><P><R>x</R></P></Doc></ReamNote>";
        rig.App.CurrentWorkspace.FocusedNote.IsDraft = false;
        rig.Prompts.Confirmations.Clear();
        rig.Prompts.ConfirmAnswer = false;

        rig.Manager.ClearReam();

        Assert.Contains("1 workspace and 1 note will be removed", Assert.Single(rig.Prompts.Confirmations));
    }
}

public class ReamCommandsAndKeysTests
{
    private sealed class ScriptedFiles : IReamFiles
    {
        public List<string> Calls { get; } = [];
        public bool Leave { get; set; } = true;
        public bool NewReam() => Record("new");
        public bool OpenReam() => Record("open");
        public bool Save() => Record("save");
        public bool SaveAs() => Record("saveAs");
        public bool ClearReam() => Record("clear");
        public void SetAutoSave(bool on) => Calls.Add($"autosave:{on}");
        public bool ConfirmLeave() => Leave;

        private bool Record(string call)
        {
            Calls.Add(call);
            return true;
        }
    }

    private static AppViewModel App() => new(new AppConfig(), [new WorkspaceViewModel("W")]);

    [Fact]
    public void EachCommand_AsksTheManager()
    {
        var app = App();
        var files = new ScriptedFiles();
        app.Files = files;

        app.NewReamCommand.Execute(null);
        app.OpenReamCommand.Execute(null);
        app.SaveReamCommand.Execute(null);
        app.SaveReamAsCommand.Execute(null);
        app.ClearReamCommand.Execute(null);
        app.ToggleAutoSaveCommand.Execute(null);

        Assert.Equal(["new", "open", "save", "saveAs", "clear", "autosave:False"], files.Calls);
    }

    [Fact]
    public void WithNoManager_TheCommandsDoNothing_InsteadOfCrashing()
    {
        var app = App();

        app.NewReamCommand.Execute(null);
        app.SaveReamCommand.Execute(null);
        app.ClearReamCommand.Execute(null);
        app.ToggleAutoSaveCommand.Execute(null);

        Assert.Null(app.Files);
    }

    [Theory]
    [InlineData("newReam", Key.N, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("openReam", Key.O, ModifierKeys.Control)]
    [InlineData("save", Key.S, ModifierKeys.Control)]
    [InlineData("saveAs", Key.S, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("clearReam", Key.Q, ModifierKeys.Alt | ModifierKeys.Shift)]
    public void TheDefaultKeys_AreBoundToTheirCommands(string action, Key key, ModifierKeys modifiers)
    {
        var app = App();

        var bindings = KeyBindingsRegistry.Build(AppConfig.DefaultKeybindings(), app.Actions);

        var binding = Assert.Single(bindings, b => b.Key == key && b.Modifiers == modifiers);
        Assert.Same(app.Actions[action], binding.Command);
    }

    [Fact]
    public void ClosingTheWindow_IsCancelled_WhenTheUserSaysStay() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var files = new ScriptedFiles { Leave = false };
        fx.App.Files = files;

        fx.Window.Close();
        Ui.Settle();

        Assert.True(fx.Window.IsVisible);

        files.Leave = true;
        fx.Window.Close();
        Ui.Settle();
        Assert.False(fx.Window.IsVisible);
    });
}
