using System.Windows.Threading;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Models;
using Ream.Persistence;
using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>Scripted answers for the dialogs and prompts, and a record of what was asked.</summary>
internal sealed class FakeDialogs : IFileDialogs
{
    public Queue<string?> NewAnswers { get; } = new();
    public Queue<string?> OpenAnswers { get; } = new();
    public Queue<string?> SaveAsAnswers { get; } = new();
    public List<string> SuggestedNames { get; } = [];

    public string? PickNewReam(string suggestedName, string initialDirectory)
    {
        SuggestedNames.Add(suggestedName);
        return NewAnswers.Count > 0 ? NewAnswers.Dequeue() : null;
    }

    public string? PickOpenReam(string initialDirectory) => OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : null;

    public string? PickSaveAs(string suggestedName, string initialDirectory)
    {
        SuggestedNames.Add(suggestedName);
        return SaveAsAnswers.Count > 0 ? SaveAsAnswers.Dequeue() : null;
    }
}

internal sealed class FakePrompts : IUserPrompts
{
    public SaveChoice SaveAnswer { get; set; } = SaveChoice.Cancel;
    public bool ConfirmAnswer { get; set; }
    public List<string> SaveQuestions { get; } = [];
    public List<string> Confirmations { get; } = [];
    public List<string> Errors { get; } = [];

    public SaveChoice AskSaveChanges(string reamName)
    {
        SaveQuestions.Add(reamName);
        return SaveAnswer;
    }

    public bool Confirm(string title, string message, string confirmText)
    {
        Confirmations.Add(message);
        return ConfirmAnswer;
    }

    public void ShowError(string title, string message) => Errors.Add(message);
}

/// <summary>A running ream (in a temp folder) with a manager and scripted dialogs, for the tests of New / Open / Save / Save As / Clear.</summary>
internal sealed class ManagerRig : IDisposable
{
    public ManagerRig(bool autoSave = true, bool tutorialOnNew = true)
    {
        Dir = new TempDir();
        ConfigPath = Dir.Combine("config.json");
        Store = new AppConfigStore(ConfigPath);
        Store.Load();

        Config = new AppConfig { AutoSave = autoSave, TutorialOnNew = tutorialOnNew };
        Dialogs = new FakeDialogs();
        Prompts = new FakePrompts();

        var launch = ReamLauncher.Create(Dir.Combine("reams", "First.ream"), Config, ConfigPath);
        App = new AppViewModel(Config, [], 0, launch.Repository);
        SnapshotMapper.LoadInto(App, launch.Snapshot, launch.Repository);
        Manager = new ReamManager(
            new ReamSession(launch.Repository, App, Dispatcher.CurrentDispatcher, autoSave),
            App, Store, Dialogs, Prompts, Dispatcher.CurrentDispatcher, ConfigPath);
    }

    public TempDir Dir { get; }
    public string ConfigPath { get; }
    public AppConfigStore Store { get; }
    public AppConfig Config { get; }
    public FakeDialogs Dialogs { get; }
    public FakePrompts Prompts { get; }
    public AppViewModel App { get; }
    public ReamManager Manager { get; }

    public string ReamsFolder => Dir.Combine("reams");

    public string PathOf(string name) => Path.Combine(ReamsFolder, name + ReamPaths.Extension);

    public NoteViewModel FirstNote => App.Workspaces.First(w => w.Notes.Count > 0).Notes[0];

    public void Dispose()
    {
        Manager.Current.Dispose();
        Dir.Dispose();
    }
}

public class ReamManagerNewTests
{
    private static int RealWorkspaces(AppViewModel app) => app.Workspaces.Count(w => !w.IsEdge);

    [Fact]
    public void New_CreatesTheReam_SwitchesToItWithTheTutorial_AndRemembersIt()
    {
        using var rig = new ManagerRig();
        Directory.CreateDirectory(rig.ReamsFolder);
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));

        Assert.True(rig.Manager.NewReam());

        Assert.True(File.Exists(rig.PathOf("Second")));
        Assert.True(Directory.Exists(Path.Combine(rig.ReamsFolder, "Second")));
        Assert.Equal("Second", rig.App.ReamName);
        Assert.Equal("Ream - Second", rig.App.WindowTitle);
        Assert.Equal(3, RealWorkspaces(rig.App));
        Assert.Equal(rig.PathOf("Second"), rig.Manager.Current.ReamPath);
        Assert.Equal(rig.PathOf("Second"), rig.App.Config.LastReam);
        Assert.True(rig.Store.TryLoad(out var saved, out _));
        Assert.Equal(rig.PathOf("Second"), saved.LastReam);
        Assert.Empty(rig.Prompts.Errors);
    }

    [Fact]
    public void ACancelledDialog_ChangesNothing()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.NewAnswers.Enqueue(null);

        Assert.False(rig.Manager.NewReam());

        Assert.Equal("First", rig.App.ReamName);
        Assert.Empty(rig.Prompts.Errors);
    }

    [Fact]
    public void ANameThatIsTaken_IsRefusedWithAnExplanation_AndTheExistingReamIsUntouched()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("First"));
        byte[] before = File.ReadAllBytes(rig.PathOf("First"));

        Assert.False(rig.Manager.NewReam());

        Assert.Contains("already a ream called \"First\"", Assert.Single(rig.Prompts.Errors));
        Assert.Equal("First", rig.App.ReamName);
        Assert.Equal(before, File.ReadAllBytes(rig.PathOf("First")));
    }

    [Fact]
    public void ADataFolderThatIsTaken_CountsAsTaken_Too()
    {
        using var rig = new ManagerRig();
        Directory.CreateDirectory(Path.Combine(rig.ReamsFolder, "Second"));
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));

        Assert.False(rig.Manager.NewReam());

        Assert.Single(rig.Prompts.Errors);
        Assert.False(File.Exists(rig.PathOf("Second")));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("bad<name")]
    [InlineData("trailing.")]
    public void ANameWindowsCannotUse_IsRefused(string name)
    {
        using var rig = new ManagerRig();
        rig.Dialogs.NewAnswers.Enqueue(Path.Combine(rig.ReamsFolder, name + ".ream"));

        Assert.False(rig.Manager.NewReam());

        Assert.Single(rig.Prompts.Errors);
        Assert.Equal("First", rig.App.ReamName);
    }

    [Fact]
    public void AFolderThatDoesNotExist_IsRefused()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.NewAnswers.Enqueue(Path.Combine(rig.Dir.Combine("nowhere"), "X.ream"));

        Assert.False(rig.Manager.NewReam());

        Assert.Contains("doesn't exist", Assert.Single(rig.Prompts.Errors));
    }

    [Fact]
    public void AMissingExtension_IsAdded()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.NewAnswers.Enqueue(Path.Combine(rig.ReamsFolder, "Plain"));

        Assert.True(rig.Manager.NewReam());

        Assert.True(File.Exists(rig.PathOf("Plain")));
    }

    [Fact]
    public void WithTheTutorialOff_NewMakesAnEmptyReamWithADraftReady()
    {
        using var rig = new ManagerRig(tutorialOnNew: false);
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Blank"));

        Assert.True(rig.Manager.NewReam());

        Assert.Equal(0, RealWorkspaces(rig.App) - 1); // only the workspace holding the draft
        var draft = Assert.Single(rig.App.CurrentWorkspace.Notes);
        Assert.True(draft.IsDraft);
        Assert.Empty(new DocumentRepository(rig.PathOf("Blank")).Load().Workspaces);
        Assert.False(rig.Manager.Current.HasUnsavedChanges);
    }

    [Fact]
    public void TheSuggestedNameIsFreeInTheCurrentFolder()
    {
        using var rig = new ManagerRig();
        rig.Dialogs.NewAnswers.Enqueue(null);

        rig.Manager.NewReam();

        Assert.Equal("Untitled.ream", Assert.Single(rig.Dialogs.SuggestedNames));
    }

    [Fact]
    public void WithAutoSaveOn_PendingEditsGoToTheOldReamBeforeItIsLeft()
    {
        using var rig = new ManagerRig(autoSave: true);
        rig.FirstNote.Body = "typed just now";
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));

        Assert.True(rig.Manager.NewReam());

        var old = new DocumentRepository(rig.PathOf("First")).Load();
        Assert.Contains(old.Workspaces.SelectMany(w => w.Notes), n => n.Body == "typed just now");
    }

    [Fact]
    public void WithAutoSaveOff_UnsavedChangesAskFirst_AndSaveWritesThemBeforeSwitching()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "unsaved words";
        rig.Prompts.SaveAnswer = SaveChoice.Save;
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));

        Assert.True(rig.Manager.NewReam());

        Assert.Equal(["First"], rig.Prompts.SaveQuestions);
        Assert.Contains(new DocumentRepository(rig.PathOf("First")).Load().Workspaces.SelectMany(w => w.Notes), n => n.Body == "unsaved words");
        Assert.Equal("Second", rig.App.ReamName);
    }

    [Fact]
    public void WithAutoSaveOff_DontSaveDiscardsThem_AndCancelStaysPut()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "unsaved words";
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));
        rig.Prompts.SaveAnswer = SaveChoice.Cancel;

        Assert.False(rig.Manager.NewReam());
        Assert.Equal("First", rig.App.ReamName);
        Assert.False(File.Exists(rig.PathOf("Second")));
        Assert.Equal("unsaved words", rig.FirstNote.Body);

        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));
        rig.Prompts.SaveAnswer = SaveChoice.DontSave;
        Assert.True(rig.Manager.NewReam());

        Assert.DoesNotContain(new DocumentRepository(rig.PathOf("First")).Load().Workspaces.SelectMany(w => w.Notes), n => n.Body == "unsaved words");
    }

    [Fact]
    public void WithAutoSaveOff_NothingIsAskedWhenNothingChanged_EvenAfterMovingAround()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.App.SwitchWorkspace(1);
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));

        Assert.True(rig.Manager.NewReam());

        Assert.Empty(rig.Prompts.SaveQuestions);
    }

    [Fact]
    public void AfterNew_EditsGoToTheNewReamOnly()
    {
        using var rig = new ManagerRig(autoSave: false);
        byte[] firstBefore = File.ReadAllBytes(rig.PathOf("First"));
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));
        Assert.True(rig.Manager.NewReam());

        rig.FirstNote.Body = "in the second";
        Assert.True(rig.Manager.Current.Save());

        Assert.Contains(new DocumentRepository(rig.PathOf("Second")).Load().Workspaces.SelectMany(w => w.Notes), n => n.Body == "in the second");
        Assert.Equal(firstBefore, File.ReadAllBytes(rig.PathOf("First")));
        Assert.DoesNotContain(new DocumentRepository(rig.PathOf("First")).Load().Workspaces.SelectMany(w => w.Notes), n => n.Body == "in the second");
    }

    [Fact]
    public void APickedPathIsCheckedBeforeAnythingIsAsked_SoARefusedNameNeverPromptsToSave()
    {
        using var rig = new ManagerRig(autoSave: false);
        rig.FirstNote.Body = "unsaved words";
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("First")); // taken

        Assert.False(rig.Manager.NewReam());

        Assert.Empty(rig.Prompts.SaveQuestions);
        Assert.Equal("unsaved words", rig.FirstNote.Body);
    }
}

/// <summary>The File tab's Recent list (AppConfig.RecentReams): built and persisted by RecordLastReam as reams are opened/created/saved-as.</summary>
public class RecentReamsTests
{
    [Fact]
    public void OpeningAndCreatingReams_BuildsTheRecentList_MostRecentFirst()
    {
        using var rig = new ManagerRig();
        Directory.CreateDirectory(rig.ReamsFolder);
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));
        Assert.True(rig.Manager.NewReam());

        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Third"));
        Assert.True(rig.Manager.NewReam());

        Assert.Equal([rig.PathOf("Third"), rig.PathOf("Second")], rig.App.Config.RecentReams);
    }

    [Fact]
    public void ReopeningAReamAlreadyInTheList_MovesItToTheFront_WithoutDuplicating()
    {
        using var rig = new ManagerRig();
        Directory.CreateDirectory(rig.ReamsFolder);
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));
        Assert.True(rig.Manager.NewReam());
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Third"));
        Assert.True(rig.Manager.NewReam());

        Assert.True(rig.Manager.OpenReam(rig.PathOf("Second")));

        Assert.Equal([rig.PathOf("Second"), rig.PathOf("Third")], rig.App.Config.RecentReams);
    }

    [Fact]
    public void TheRecentList_IsCappedAtTheLimit()
    {
        using var rig = new ManagerRig();
        Directory.CreateDirectory(rig.ReamsFolder);

        for (int i = 0; i < AppConfig.RecentReamsLimit + 3; i++)
        {
            rig.Dialogs.NewAnswers.Enqueue(rig.PathOf($"Ream{i}"));
            Assert.True(rig.Manager.NewReam());
        }

        Assert.Equal(AppConfig.RecentReamsLimit, rig.App.Config.RecentReams.Count);
        Assert.Equal(rig.PathOf($"Ream{AppConfig.RecentReamsLimit + 2}"), rig.App.Config.RecentReams[0]);
    }

    [Fact]
    public void TheRecentList_IsSavedAlongsideLastReam()
    {
        using var rig = new ManagerRig();
        Directory.CreateDirectory(rig.ReamsFolder);
        rig.Dialogs.NewAnswers.Enqueue(rig.PathOf("Second"));

        Assert.True(rig.Manager.NewReam());

        Assert.True(rig.Store.TryLoad(out var saved, out var error), error);
        Assert.Equal(rig.App.Config.RecentReams, saved.RecentReams);
        Assert.Equal(rig.App.Config.LastReam, saved.LastReam);
    }
}

public class ReamLauncherTests
{
    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Dir = new TempDir();
            Paths = new AppPaths(Dir.Combine("config.json"), Dir.Combine("ReemDocuments"), Dir.Combine("Ream"));
        }

        public TempDir Dir { get; }
        public AppPaths Paths { get; }
        public void Dispose() => Dir.Dispose();

        /// <summary>An old-format folder: metadata.json, one workspace with layout.json and one note.</summary>
        public string LegacyFolder(string? at = null, int schema = 1)
        {
            string folder = at ?? Paths.DefaultDocumentsRoot;
            Guid workspace = Guid.NewGuid(), note = Guid.NewGuid();
            string ws = Path.Combine(folder, "ws-11111111");
            Directory.CreateDirectory(ws);
            File.WriteAllText(Path.Combine(folder, "metadata.json"),
                $$"""{ "schemaVersion": {{schema}}, "workspaces": [ { "id": "{{workspace}}", "name": "Old", "folderName": "ws-11111111", "order": 0 } ], "currentWorkspaceId": "{{workspace}}" }""");
            File.WriteAllText(Path.Combine(ws, "layout.json"),
                $$"""{ "schemaVersion": 1, "notes": [ { "noteId": "{{note}}", "fileName": "{{note:N}}.reamnote", "title": "old note", "widthFraction": 0.5 } ] }""");
            File.WriteAllText(Path.Combine(ws, note.ToString("N") + ".reamnote"), "old note text");
            return folder;
        }
    }

    [Fact]
    public void AFreshInstall_MakesTheTutorialReamInTheDefaultFolder()
    {
        using var rig = new Rig();

        var result = ReamLauncher.Launch(new AppConfig(), rig.Paths);

        Assert.Equal(Path.Combine(rig.Paths.DefaultReamsFolder, "My Ream.ream"), result.Repository.ReamPath);
        Assert.Null(result.Warning);
        Assert.Equal(3, result.Snapshot.Workspaces.Count);
        Assert.Equal(8, result.Snapshot.Workspaces.Sum(w => w.Notes.Count));
        Assert.True(File.Exists(result.Repository.ReamPath));
    }

    [Fact]
    public void ATakenDefaultName_GetsANumber()
    {
        using var rig = new Rig();
        Directory.CreateDirectory(rig.Paths.DefaultReamsFolder);
        File.WriteAllText(Path.Combine(rig.Paths.DefaultReamsFolder, "My Ream.ream"), "{}");

        var result = ReamLauncher.Launch(new AppConfig(), rig.Paths);

        Assert.Equal(Path.Combine(rig.Paths.DefaultReamsFolder, "My Ream 2.ream"), result.Repository.ReamPath);
    }

    [Fact]
    public void WithTheTutorialOff_AFreshInstallGetsAnEmptyReam()
    {
        using var rig = new Rig();

        var result = ReamLauncher.Launch(new AppConfig { TutorialOnNew = false }, rig.Paths);

        Assert.Empty(result.Snapshot.Workspaces);
        Assert.True(File.Exists(result.Repository.ReamPath));
    }

    [Fact]
    public void TheLastReam_IsReopened_WithoutMakingAnotherOne()
    {
        using var rig = new Rig();
        var first = ReamLauncher.Launch(new AppConfig(), rig.Paths);

        var second = ReamLauncher.Launch(new AppConfig().WithLastReam(first.Repository.ReamPath), rig.Paths);

        Assert.Equal(first.Repository.ReamPath, second.Repository.ReamPath);
        Assert.Null(second.Warning);
        Assert.Equal(first.Snapshot.Workspaces.Select(w => w.Id), second.Snapshot.Workspaces.Select(w => w.Id));
        Assert.Single(Directory.GetFiles(rig.Paths.DefaultReamsFolder, "*.ream"));
    }

    [Fact]
    public void ALastReamThatWentMissing_IsExplained_AndANewOneStarts()
    {
        using var rig = new Rig();

        var result = ReamLauncher.Launch(new AppConfig().WithLastReam(rig.Dir.Combine("gone", "Ghost.ream")), rig.Paths);

        Assert.Contains("isn't there any more", result.Warning);
        Assert.Equal("My Ream", result.Repository.Name);
    }

    [Fact]
    public void ALastReamFromANewerRelease_IsExplained_AndLeftAlone()
    {
        using var rig = new Rig();
        string future = rig.Dir.Combine("Future.ream");
        File.WriteAllText(future, """{ "schemaVersion": 99, "workspaces": [] }""");

        var result = ReamLauncher.Launch(new AppConfig().WithLastReam(future), rig.Paths);

        Assert.Contains("couldn't open", result.Warning);
        Assert.Equal("""{ "schemaVersion": 99, "workspaces": [] }""", File.ReadAllText(future));
        Assert.Equal("My Ream", result.Repository.Name);
    }

    [Fact]
    public void AnOldFormatFolder_IsConvertedInPlaceAndOpened_WithABackup()
    {
        using var rig = new Rig();
        string folder = rig.LegacyFolder();

        var result = ReamLauncher.Launch(new AppConfig(), rig.Paths);

        Assert.Equal(Path.Combine(folder, "ReemDocuments.ream"), result.Repository.ReamPath);
        Assert.Null(result.Warning);
        Assert.Equal("old note text", result.Snapshot.Workspaces.Single().Notes.Single().Body);
        Assert.False(File.Exists(Path.Combine(folder, "metadata.json")));
        Assert.True(Directory.GetDirectories(rig.Dir.Path, "ReemDocuments-backup-*").Length == 1);
        Assert.False(Directory.Exists(rig.Paths.DefaultReamsFolder)); // no tutorial ream was made
    }

    [Fact]
    public void TheDocumentsRootFromConfig_IsPreferredOverTheDefaultFolder()
    {
        using var rig = new Rig();
        string custom = rig.LegacyFolder(rig.Dir.Combine("Elsewhere"));

        var result = ReamLauncher.Launch(new AppConfig { DocumentsRoot = custom }, rig.Paths);

        Assert.Equal(Path.Combine(custom, "Elsewhere.ream"), result.Repository.ReamPath);
    }

    [Fact]
    public void AFolderThatWasConvertedBefore_ButNeverRemembered_IsJustOpened()
    {
        using var rig = new Rig();
        rig.LegacyFolder();
        var first = ReamLauncher.Launch(new AppConfig(), rig.Paths);

        var second = ReamLauncher.Launch(new AppConfig(), rig.Paths); // lastReam was never recorded

        Assert.Equal(first.Repository.ReamPath, second.Repository.ReamPath);
        Assert.Single(Directory.GetDirectories(rig.Dir.Path, "ReemDocuments-backup-*")); // and no second backup
    }

    [Fact]
    public void AnOldFolderFromANewerRelease_IsLeftAlone_AndANewReamStarts()
    {
        using var rig = new Rig();
        string folder = rig.LegacyFolder(schema: 99);

        var result = ReamLauncher.Launch(new AppConfig(), rig.Paths);

        Assert.Contains("couldn't convert", result.Warning);
        Assert.True(File.Exists(Path.Combine(folder, "metadata.json")));
        Assert.Equal("My Ream", result.Repository.Name);
    }

    [Fact]
    public void TryOpen_ReportsAProblemInsteadOfThrowing()
    {
        using var rig = new Rig();
        string future = rig.Dir.Combine("Future.ream");
        File.WriteAllText(future, """{ "schemaVersion": 99 }""");

        Assert.False(ReamLauncher.TryOpen(future, out var result, out string? error));

        Assert.Null(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
