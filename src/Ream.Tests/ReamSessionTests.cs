using System.Windows.Threading;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Abstractions;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class SnapshotFingerprintTests
{
    private static NoteSnapshot Note(string id = "11111111111111111111111111111111", string title = "T", string body = "b", double width = 0.5, bool fullscreen = false, string? custom = null) =>
        new(Guid.Parse(id), title, body, width, fullscreen, custom);

    private static WorkspaceSnapshot Workspace(string id = "22222222222222222222222222222222", string? name = "W", params NoteSnapshot[] notes) =>
        new(Guid.Parse(id), name, "ws-22222222", notes, notes.Length > 0 ? notes[0].Id : null);

    private static string Of(Guid? current, params WorkspaceSnapshot[] workspaces) =>
        SnapshotFingerprint.Of(new DocumentSnapshot(workspaces, current));

    [Fact]
    public void TheSameContent_HasTheSameFingerprint() =>
        Assert.Equal(Of(null, Workspace(notes: Note())), Of(null, Workspace(notes: Note())));

    [Fact]
    public void WhereYouAre_IsNotContent()
    {
        var a = Note("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var b = Note("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var w1 = Workspace(notes: [a, b]) with { FocusedNoteId = a.Id };
        var w2 = Workspace(notes: [a, b]) with { FocusedNoteId = b.Id };

        Assert.Equal(Of(null, w1), Of(w1.Id, w2));
        Assert.Equal(Of(null, Workspace(notes: Note(fullscreen: false))), Of(null, Workspace(notes: Note(fullscreen: true))));
    }

    [Fact]
    public void AnyEditToTheContent_ChangesIt()
    {
        string baseline = Of(null, Workspace(notes: Note()));

        Assert.NotEqual(baseline, Of(null, Workspace(notes: Note(body: "b!"))));
        Assert.NotEqual(baseline, Of(null, Workspace(notes: Note(title: "T2"))));
        Assert.NotEqual(baseline, Of(null, Workspace(notes: Note(width: 0.6))));
        Assert.NotEqual(baseline, Of(null, Workspace(notes: Note(custom: "mine"))));
        Assert.NotEqual(baseline, Of(null, Workspace(name: "Renamed", notes: Note())));
        Assert.NotEqual(baseline, Of(null, Workspace(notes: [Note(), Note("cccccccccccccccccccccccccccccccc")])));
        Assert.NotEqual(baseline, Of(null));
    }

    [Fact]
    public void OrderMatters_ForNotesAndWorkspaces()
    {
        var a = Note("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var b = Note("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        Assert.NotEqual(Of(null, Workspace(notes: [a, b])), Of(null, Workspace(notes: [b, a])));

        var w1 = Workspace("11111111111111111111111111111111");
        var w2 = Workspace("33333333333333333333333333333333");
        Assert.NotEqual(Of(null, w1, w2), Of(null, w2, w1));
    }

    [Fact]
    public void NullAndEmptyTitlesAreDifferent_AndFieldsCannotBleedIntoEachOther()
    {
        Assert.NotEqual(Of(null, Workspace(notes: Note(custom: null))), Of(null, Workspace(notes: Note(custom: ""))));
        Assert.NotEqual(Of(null, Workspace(notes: Note(title: "ab", body: "c"))), Of(null, Workspace(notes: Note(title: "a", body: "bc"))));
    }
}

public class PersistenceCoordinatorTests
{
    private static AppViewModel MakeApp(IAssetStore? assets, params (string? Name, int Notes)[] spec)
    {
        var workspaces = spec.Select(s =>
        {
            var workspace = new WorkspaceViewModel(s.Name, assets);
            workspace.LoadNotes(Enumerable.Range(0, s.Notes).Select(i => new NoteViewModel { Title = $"n{i}", Body = $"body {i}" }), null);
            return workspace;
        }).ToList();
        return new AppViewModel(new AppConfig(), workspaces, 0, assets);
    }

    private static int NoteFiles(string root) =>
        Directory.Exists(root) ? Directory.GetFiles(root, "*" + ReamPaths.NoteExtension, SearchOption.AllDirectories).Length : 0;

    private sealed class Rig : IDisposable
    {
        private readonly TempDir _dir = new();

        public Rig(bool autoSave, bool startClean = true, params (string? Name, int Notes)[] spec)
        {
            Root = _dir.Combine("ream");
            Repo = TestReam.Repo(Root);
            App = MakeApp(Repo, spec.Length == 0 ? [("A", 2), ("B", 1)] : spec);
            Coordinator = new PersistenceCoordinator(Repo, App, Dispatcher.CurrentDispatcher, autoSave, startClean);
        }

        public string Root { get; }
        public DocumentRepository Repo { get; }
        public AppViewModel App { get; }
        public PersistenceCoordinator Coordinator { get; }
        public NoteViewModel FirstNote => App.CurrentWorkspace.Notes[0];

        public void Dispose()
        {
            Coordinator.Dispose();
            _dir.Dispose();
        }
    }

    [Fact]
    public void WithAutoSaveOff_AnEditIsUnsaved_AndNothingIsWritten()
    {
        using var rig = new Rig(autoSave: false);
        Assert.False(rig.Coordinator.HasUnsavedChanges);

        rig.FirstNote.Body = "changed";
        rig.Coordinator.Evaluate();

        Assert.True(rig.Coordinator.HasUnsavedChanges);
        rig.Coordinator.Flush(); // exit with auto-save off writes nothing
        Assert.False(File.Exists(TestReam.FileIn(rig.Root)));
        Assert.Equal(0, NoteFiles(rig.Root));
    }

    [Fact]
    public void Save_WritesEvenWithAutoSaveOff_AndClearsTheFlag()
    {
        using var rig = new Rig(autoSave: false);
        rig.FirstNote.Body = "changed";
        rig.Coordinator.Evaluate();

        Assert.True(rig.Coordinator.Save());

        Assert.False(rig.Coordinator.HasUnsavedChanges);
        Assert.True(File.Exists(TestReam.FileIn(rig.Root)));
        Assert.Equal(3, NoteFiles(rig.Root));
        Assert.Contains(TestReam.Repo(rig.Root).Load().Workspaces.SelectMany(w => w.Notes), n => n.Body == "changed");
    }

    [Fact]
    public void MovingAround_IsNotAnEdit()
    {
        using var rig = new Rig(autoSave: false);

        rig.App.CurrentWorkspace.FocusBy(1);
        rig.App.SwitchWorkspace(1);
        rig.App.SwitchWorkspace(-1);
        rig.Coordinator.Evaluate();

        Assert.False(rig.Coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void TheEmptyEdgesAndABlankDraft_AreNotEdits()
    {
        using var rig = new Rig(autoSave: false);

        rig.App.NewNoteCommand.Execute(null); // a blank draft, and a new empty workspace at an edge if needed
        rig.App.SwitchWorkspace(-1);
        rig.Coordinator.Evaluate();

        Assert.False(rig.Coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void PuttingTheTextBack_IsNoLongerAnEdit()
    {
        using var rig = new Rig(autoSave: false);
        string original = rig.FirstNote.Body;

        rig.FirstNote.Body = "changed";
        rig.Coordinator.Evaluate();
        Assert.True(rig.Coordinator.HasUnsavedChanges);

        rig.FirstNote.Body = original;
        rig.Coordinator.Evaluate();
        Assert.False(rig.Coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void ARenameOrAResize_IsAnEdit()
    {
        using var rig = new Rig(autoSave: false);

        rig.App.CurrentWorkspace.Name = "Renamed";
        rig.Coordinator.Evaluate();
        Assert.True(rig.Coordinator.HasUnsavedChanges);
        rig.Coordinator.Save();
        Assert.False(rig.Coordinator.HasUnsavedChanges);

        rig.FirstNote.WidthFraction = 0.8;
        rig.Coordinator.Evaluate();
        Assert.True(rig.Coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void AddingOrClosingANote_IsAnEdit()
    {
        using var rig = new Rig(autoSave: false);

        rig.App.CloseNoteCommand.Execute(null);
        rig.Coordinator.Evaluate();

        Assert.True(rig.Coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void WithAutoSaveOn_FlushWritesWhatIsPending()
    {
        using var rig = new Rig(autoSave: true);
        rig.FirstNote.Body = "typed";

        rig.Coordinator.Flush();

        Assert.Equal(3, NoteFiles(rig.Root));
        Assert.Contains(TestReam.Repo(rig.Root).Load().Workspaces.SelectMany(w => w.Notes), n => n.Body == "typed");
        Assert.Null(rig.Coordinator.LastError);
    }

    [Fact]
    public void TurningAutoSaveOn_SavesWhatWasWaiting()
    {
        using var rig = new Rig(autoSave: false);
        rig.FirstNote.Body = "waiting";
        rig.Coordinator.Flush();
        Assert.Equal(0, NoteFiles(rig.Root));

        rig.Coordinator.AutoSave = true;
        rig.Coordinator.Flush();

        Assert.Equal(3, NoteFiles(rig.Root));
    }

    [Fact]
    public void StateThatOnlyExistsInMemory_StartsUnsaved_UntilTheFirstSave()
    {
        using var rig = new Rig(autoSave: false, startClean: false);
        Assert.True(rig.Coordinator.HasUnsavedChanges);

        Assert.True(rig.Coordinator.Save());

        Assert.False(rig.Coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void TheFlagFlipsAreAnnounced()
    {
        using var rig = new Rig(autoSave: false);
        var flips = new List<bool>();
        rig.Coordinator.UnsavedChangesChanged += flips.Add;

        rig.FirstNote.Body = "one";
        rig.Coordinator.Evaluate();
        rig.FirstNote.Body = "two";
        rig.Coordinator.Evaluate(); // still unsaved: no second announcement
        rig.Coordinator.Save();

        Assert.Equal([true, false], flips);
    }

    [Fact]
    public void AfterDispose_NothingIsWatchedAnyMore()
    {
        using var rig = new Rig(autoSave: true);
        rig.Coordinator.Dispose();

        rig.FirstNote.Body = "too late";
        rig.Coordinator.Flush();

        Assert.Equal(0, NoteFiles(rig.Root));
    }

    private sealed class FailingRepository : IDocumentRepository
    {
        public DocumentSnapshot Load() => new([], null);
        public void Save(DocumentSnapshot snapshot) => throw new IOException("disk on fire");
    }

    [Fact]
    public void AFailedSave_ReturnsFalse_KeepsTheError_AndLeavesTheChangesUnsaved()
    {
        var app = MakeApp(null, ("A", 1));
        using var coordinator = new PersistenceCoordinator(new FailingRepository(), app, Dispatcher.CurrentDispatcher, autoSave: false);
        app.CurrentWorkspace.Notes[0].Body = "x";

        Assert.False(coordinator.Save());

        Assert.IsType<IOException>(coordinator.LastError);
        Assert.True(coordinator.HasUnsavedChanges);
    }
}

public class LoadReamTests
{
    private static WorkspaceViewModel Workspace(string? name, params string[] bodies)
    {
        var workspace = new WorkspaceViewModel(name);
        workspace.LoadNotes(bodies.Select((b, i) => new NoteViewModel { Title = $"{name}{i}", Body = b }), null);
        return workspace;
    }

    private static AppViewModel App(params WorkspaceViewModel[] workspaces) => new(new AppConfig(), workspaces);

    [Fact]
    public void ItReplacesWhatWasOpen_AndKeepsTheEmptyEdges()
    {
        var old = Workspace("Old", "x");
        var app = App(old);
        var b1 = Workspace("B1", "1");
        var b2 = Workspace("B2", "2", "3");

        app.LoadReam([b1, b2], 1, null);

        Assert.DoesNotContain(old, app.Workspaces);
        Assert.Equal([true, false, false, true], app.Workspaces.Select(w => w.IsEmpty));
        Assert.Same(b2, app.CurrentWorkspace);
        Assert.True(b1.IsCurrent == false && b2.IsCurrent);
    }

    [Fact]
    public void AnEmptyReam_LandsOnAWorkspaceWithADraftInIt()
    {
        var app = App(Workspace("Old", "x"));

        app.LoadReam([], 0, null);

        var draft = Assert.Single(app.CurrentWorkspace.Notes);
        Assert.True(draft.IsDraft);
        Assert.Equal(3, app.Workspaces.Count);
        Assert.True(app.Workspaces[0].IsEmpty && app.Workspaces[^1].IsEmpty);
    }

    [Fact]
    public void TheStripSnaps_AndIsBackToNormalAfterwards()
    {
        var app = App(Workspace("Old", "x"));
        var seen = new List<bool>();
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.SuppressAnimation)) seen.Add(app.SuppressAnimation);
        };

        app.LoadReam([Workspace("New", "y")], 0, null);

        Assert.True(seen.FirstOrDefault());
        Assert.False(app.SuppressAnimation);
    }

    [Fact]
    public void WhatTheViewsBindTo_IsAnnounced()
    {
        var app = App(Workspace("Old", "x"));
        var changed = new List<string?>();
        app.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        app.LoadReam([Workspace("Fresh", "y")], 0, null);

        Assert.Contains(nameof(AppViewModel.CurrentWorkspace), changed);
        Assert.Contains(nameof(AppViewModel.WorkspaceLabel), changed);
        Assert.Equal("Fresh", app.WorkspaceLabel);
    }

    [Fact]
    public void ANewNoteAfterwards_IsNumberedAfterTheNotesThatCameIn()
    {
        var app = App(Workspace("Old", "x"));

        app.LoadReam([Workspace("New", "1", "2", "3")], 0, null);
        app.FocusNextNoteCommand.Execute(null);
        app.FocusNextNoteCommand.Execute(null);
        app.FocusNextNoteCommand.Execute(null); // past the end: a draft

        Assert.EndsWith("4", app.CurrentWorkspace.FocusedNote!.Title);
    }

    [Fact]
    public void TheTitle_IsJustReamWithoutAReam_ThenNamesIt_AndStarsUnsavedChangesOnlyWithAutoSaveOff()
    {
        var app = App(Workspace("A", "x"));
        Assert.Equal("Ream", app.WindowTitle);

        app.ReamName = "Foo";
        Assert.Equal("Ream - Foo", app.WindowTitle);

        app.HasUnsavedChanges = true;
        Assert.Equal("Ream - Foo", app.WindowTitle); // auto-save on: it is on its way to disk

        app.AutoSave = false;
        Assert.Equal("Ream - Foo *", app.WindowTitle);

        app.HasUnsavedChanges = false;
        Assert.Equal("Ream - Foo", app.WindowTitle);
    }

    [Fact]
    public void TheTitleIsAnnouncedWhenAnyOfItsPartsChange()
    {
        var app = App(Workspace("A", "x"));
        int announced = 0;
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.WindowTitle)) announced++;
        };

        app.ReamName = "Foo";
        app.AutoSave = false;
        app.HasUnsavedChanges = true;

        Assert.Equal(3, announced);
    }
}

public class ReamSessionTests
{
    private static AppViewModel MakeApp(IAssetStore assets)
    {
        var workspace = new WorkspaceViewModel("A", assets);
        workspace.LoadNotes([new NoteViewModel { Title = "n", Body = "body" }], null);
        return new AppViewModel(new AppConfig(), [workspace], 0, assets);
    }

    [Fact]
    public void ItTellsTheViewModelTheReamsNameAndPath()
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Foo.ream"));
        var app = MakeApp(repo);

        using var session = new ReamSession(repo, app, Dispatcher.CurrentDispatcher, autoSave: true);

        Assert.Equal("Foo", app.ReamName);
        Assert.Equal(repo.ReamPath, app.ReamPath);
        Assert.Equal("Ream - Foo", app.WindowTitle);
        Assert.Equal("Foo", session.Name);
    }

    [Fact]
    public void WithAutoSaveOff_TheTitleGetsAStar_UntilSaved()
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Foo.ream"));
        var app = MakeApp(repo);
        using var session = new ReamSession(repo, app, Dispatcher.CurrentDispatcher, autoSave: false);

        app.CurrentWorkspace.Notes[0].Body = "edited";
        session.Coordinator.Evaluate();
        Assert.Equal("Ream - Foo *", app.WindowTitle);
        Assert.True(session.HasUnsavedChanges);

        Assert.True(session.Save());
        Assert.Equal("Ream - Foo", app.WindowTitle);
        Assert.False(app.HasUnsavedChanges);
    }

    [Fact]
    public void TheAutoSaveSetting_ReachesTheCoordinatorAndTheTitle()
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Foo.ream"));
        var app = MakeApp(repo);
        using var session = new ReamSession(repo, app, Dispatcher.CurrentDispatcher, autoSave: true);

        session.AutoSave = false;

        Assert.False(session.Coordinator.AutoSave);
        Assert.False(app.AutoSave);
    }

    [Fact]
    public void OpeningAnotherReamInPlace_KeepsEachOnesFilesSeparate()
    {
        using var dir = new TempDir();
        var repoA = new DocumentRepository(dir.Combine("A.ream"));
        var repoB = new DocumentRepository(dir.Combine("B.ream"));

        var appA = MakeApp(repoA);
        using (var seed = new ReamSession(repoA, appA, Dispatcher.CurrentDispatcher, autoSave: false, startClean: false)) seed.Save();
        var workspaceB = new WorkspaceViewModel("Other", repoB);
        workspaceB.LoadNotes([new NoteViewModel { Title = "b", Body = "in B" }], null);
        repoB.Save(SnapshotMapper.ToSnapshot(new AppViewModel(new AppConfig(), [workspaceB], 0, repoB)));

        var app = MakeApp(repoA);
        var session = new ReamSession(repoA, app, Dispatcher.CurrentDispatcher, autoSave: false);
        session.Dispose();
        SnapshotMapper.LoadInto(app, repoB.Load(), repoB);
        using var sessionB = new ReamSession(repoB, app, Dispatcher.CurrentDispatcher, autoSave: false);

        Assert.Equal("B", app.ReamName);
        Assert.Equal("in B", app.CurrentWorkspace.Notes[0].Body);
        Assert.False(sessionB.HasUnsavedChanges);

        app.CurrentWorkspace.Notes[0].Body = "edited in B";
        Assert.True(sessionB.Save());

        Assert.Equal("edited in B", new DocumentRepository(repoB.ReamPath).Load().Workspaces[0].Notes[0].Body);
        Assert.Equal("body", new DocumentRepository(repoA.ReamPath).Load().Workspaces[0].Notes[0].Body);
    }
}
