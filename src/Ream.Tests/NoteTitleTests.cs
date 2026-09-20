using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.Views;
using Ream.App.ViewModels;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class NoteTitleViewModelTests
{
    private static NoteViewModel Note(string title = "First line") => new() { Title = title };

    [Fact]
    public void WithoutACustomTitle_TheFirstLineIsShown()
    {
        var note = Note("First line");

        Assert.Equal("First line", note.DisplayTitle);
    }

    [Fact]
    public void ACustomTitle_WinsOverTheFirstLine()
    {
        var note = Note("First line");

        note.CustomTitle = "Groceries";

        Assert.Equal("Groceries", note.DisplayTitle);
        Assert.Equal("First line", note.Title);
    }

    [Fact]
    public void TheFirstLineStillTracksTheText_BehindACustomTitle()
    {
        var note = Note("Old");
        note.CustomTitle = "Mine";

        note.Title = "New first line";

        Assert.Equal("Mine", note.DisplayTitle);

        note.CustomTitle = null;
        Assert.Equal("New first line", note.DisplayTitle);
    }

    [Fact]
    public void DisplayTitleChanges_AreAnnounced()
    {
        var note = Note();
        var changed = new List<string?>();
        note.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        note.CustomTitle = "X";
        note.Title = "Y";

        Assert.Equal(2, changed.Count(name => name == nameof(NoteViewModel.DisplayTitle)));
    }

    [Fact]
    public void Editing_StartsFromWhatIsShown_AndEnterApplies()
    {
        var note = Note("First line");

        note.BeginTitleEdit();
        Assert.True(note.IsEditingTitle);
        Assert.Equal("First line", note.EditTitle);

        note.EditTitle = "  Shopping  ";
        note.CommitTitleEdit();

        Assert.False(note.IsEditingTitle);
        Assert.Equal("Shopping", note.CustomTitle);
        Assert.Equal("Shopping", note.DisplayTitle);
    }

    [Fact]
    public void EditingAnExistingCustomTitle_StartsFromIt()
    {
        var note = Note();
        note.CustomTitle = "Mine";

        note.BeginTitleEdit();

        Assert.Equal("Mine", note.EditTitle);
    }

    [Fact]
    public void ABlankTitle_GoesBackToTheFirstLine()
    {
        var note = Note("First line");
        note.CustomTitle = "Mine";

        note.BeginTitleEdit();
        note.EditTitle = "   ";
        note.CommitTitleEdit();

        Assert.Null(note.CustomTitle);
        Assert.Equal("First line", note.DisplayTitle);
    }

    [Fact]
    public void CommittingWithoutChangingAnything_DoesNotPinTheFirstLine()
    {
        var note = Note("First line");

        note.BeginTitleEdit();
        note.CommitTitleEdit();

        Assert.Null(note.CustomTitle);
    }

    [Fact]
    public void Cancelling_KeepsTheOldTitle()
    {
        var note = Note("First line");
        note.CustomTitle = "Keep";

        note.BeginTitleEdit();
        note.EditTitle = "Changed";
        note.CancelTitleEdit();
        note.CommitTitleEdit();

        Assert.Equal("Keep", note.CustomTitle);
        Assert.False(note.IsEditingTitle);
    }

    [Fact]
    public void CommittingWithoutAnEdit_DoesNothing()
    {
        var note = Note();
        note.EditTitle = "Sneaky";

        note.CommitTitleEdit();

        Assert.Null(note.CustomTitle);
    }

    [Fact]
    public void BeginningTwice_DoesNotThrowAwayWhatWasTyped()
    {
        var note = Note();
        note.BeginTitleEdit();
        note.EditTitle = "Half typed";

        note.BeginTitleEdit();

        Assert.Equal("Half typed", note.EditTitle);
    }

    [Fact]
    public void VeryLongTitles_AreCapped()
    {
        var note = Note();

        note.BeginTitleEdit();
        note.EditTitle = new string('x', 500);
        note.CommitTitleEdit();

        Assert.Equal(NoteViewModel.MaxCustomTitleLength, note.CustomTitle!.Length);
    }

    [Fact]
    public void ADraftGivenATitle_IsNoLongerADraft()
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel { Title = "a" }], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        app.NewNoteCommand.Execute(null);
        var draft = app.CurrentWorkspace.FocusedNote!;

        draft.BeginTitleEdit();
        draft.EditTitle = "Plans";
        draft.CommitTitleEdit();
        app.FocusPrevNoteCommand.Execute(null);

        Assert.Equal(2, app.CurrentWorkspace.Notes.Count);
        Assert.False(draft.IsDraft);
    }

    // ----- The F2 shortcut -----

    [Fact]
    public void RenameNote_StartsEditingTheFocusedNoteOnly()
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([Note("a"), Note("b")], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);
        app.CurrentWorkspace.SetFocus(1);

        app.RenameNoteCommand.Execute(null);

        Assert.False(app.CurrentWorkspace.Notes[0].IsEditingTitle);
        Assert.True(app.CurrentWorkspace.Notes[1].IsEditingTitle);
    }

    [Fact]
    public void RenameNote_WithNoNotes_IsHarmless()
    {
        var app = new AppViewModel(new AppConfig(), []);

        app.RenameNoteCommand.Execute(null);
    }

    [Fact]
    public void RenameNote_IsF2_AndWorkspaceRenameIsShiftF2()
    {
        var app = new AppViewModel(new AppConfig(), []);

        Assert.Equal("F2", app.Config.Keybindings["renameNote"]);
        Assert.Equal("Shift+F2", app.Config.Keybindings["renameWorkspace"]);
        Assert.Same(app.RenameNoteCommand, app.Actions["renameNote"]);
    }
}

public class NoteTitlePersistenceTests
{
    [Fact]
    public void ACustomTitle_SurvivesSaveAndLoad()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var note = new NoteSnapshot(Guid.NewGuid(), "First line", "text", 0.5, false, "My title");
        var workspace = new WorkspaceSnapshot(Guid.NewGuid(), "W", "ws-aaaaaaaa", [note], note.Id);

        new DocumentRepository(root).Save(new DocumentSnapshot([workspace], workspace.Id));
        var loaded = new DocumentRepository(root).Load().Workspaces.Single().Notes.Single();

        Assert.Equal("My title", loaded.CustomTitle);
        Assert.Equal("First line", loaded.Title);
    }

    [Fact]
    public void ANoteWithoutOne_WritesNothingExtra_AndLoadsAsNull()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var note = new NoteSnapshot(Guid.NewGuid(), "First line", "text", 0.5, false);
        var workspace = new WorkspaceSnapshot(Guid.NewGuid(), "W", "ws-aaaaaaaa", [note], note.Id);

        new DocumentRepository(root).Save(new DocumentSnapshot([workspace], workspace.Id));

        string layout = File.ReadAllText(Path.Combine(root, "ws-aaaaaaaa", "layout.json"));
        using var json = JsonDocument.Parse(layout);
        var entry = json.RootElement.GetProperty("notes")[0];
        Assert.True(!entry.TryGetProperty("customTitle", out var value) || value.ValueKind == JsonValueKind.Null);
        Assert.Null(new DocumentRepository(root).Load().Workspaces.Single().Notes.Single().CustomTitle);
    }

    [Fact]
    public void LayoutFilesFromBeforeCustomTitles_StillLoad()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var note = new NoteSnapshot(Guid.NewGuid(), "Old title", "text", 0.5, false);
        var workspace = new WorkspaceSnapshot(Guid.NewGuid(), "W", "ws-aaaaaaaa", [note], note.Id);
        new DocumentRepository(root).Save(new DocumentSnapshot([workspace], workspace.Id));

        // Strip the field, as an older build would have written the file.
        string path = Path.Combine(root, "ws-aaaaaaaa", "layout.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["notes"]![0]!.AsObject().Remove("customTitle");
        File.WriteAllText(path, node.ToJsonString());

        var loaded = new DocumentRepository(root).Load().Workspaces.Single().Notes.Single();

        Assert.Equal("Old title", loaded.Title);
        Assert.Null(loaded.CustomTitle);
    }

    [Fact]
    public void TheSnapshotMapper_CarriesItBothWays()
    {
        var workspace = new WorkspaceViewModel("W");
        workspace.LoadNotes([new NoteViewModel { Title = "First line", CustomTitle = "Mine" }], null);
        var app = new AppViewModel(new AppConfig(), [workspace]);

        var snapshot = SnapshotMapper.ToSnapshot(app);
        var note = snapshot.Workspaces.Single().Notes.Single();
        Assert.Equal("Mine", note.CustomTitle);
        Assert.Equal("First line", note.Title);

        var reloaded = SnapshotMapper.ToViewModel(snapshot, new AppConfig(), null!);
        Assert.Equal("Mine", reloaded.CurrentWorkspace.Notes.Single().DisplayTitle);
    }
}

public class NoteTitleInTheWindowTests
{
    private static TextBox BoxOf(NoteColumnView view) => Ui.Descendants<TextBox>(view).Single(b => b.Name == "TitleBox");

    private static TextBlock TextOf(NoteColumnView view) => Ui.Descendants<TextBlock>(view).Single(t => t.Name == "TitleText");

    private static void Press(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    [Fact]
    public void TheHeader_ShowsTheDisplayTitle() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);

        Assert.Equal("note 0", TextOf(view).Text);
        Assert.NotEqual(Visibility.Visible, BoxOf(view).Visibility);

        note.CustomTitle = "Renamed";
        Ui.Settle();

        Assert.Equal("Renamed", TextOf(view).Text);
    });

    [Fact]
    public void F2_ShowsATextBox_EnterAppliesIt_AndTheEditorGetsFocusBack() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);

        var binding = fx.Window.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.F2 && b.Modifiers == ModifierKeys.None);
        Assert.Same(fx.App.RenameNoteCommand, binding.Command);

        binding.Command.Execute(null);
        Ui.Settle();

        var box = BoxOf(view);
        Assert.Equal(Visibility.Visible, box.Visibility);
        Assert.Equal("note 0", box.Text);
        Assert.Same(box, Keyboard.FocusedElement);

        box.Text = "Groceries";
        Press(box, Key.Enter);
        Ui.Settle();

        Assert.Equal("Groceries", note.DisplayTitle);
        Assert.Equal(Visibility.Collapsed, box.Visibility);
        Assert.Equal("Groceries", TextOf(view).Text);
        Assert.Same(view.Editor, Keyboard.FocusedElement);
    });

    [Fact]
    public void Escape_CancelsTheEdit() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);
        fx.App.RenameNoteCommand.Execute(null);
        Ui.Settle();

        var box = BoxOf(view);
        box.Text = "Nope";
        Press(box, Key.Escape);
        Ui.Settle();

        Assert.Equal("note 0", note.DisplayTitle);
        Assert.Null(note.CustomTitle);
        Assert.False(note.IsEditingTitle);
    });

    [Fact]
    public void ClickingTheTitle_StartsAnEdit() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var second = fx.App.CurrentWorkspace.Notes[1];
        var view = fx.ColumnOf(second);

        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
        };
        TextOf(view).RaiseEvent(down);
        Ui.Settle();

        Assert.True(second.IsEditingTitle);
        Assert.Equal(Visibility.Visible, BoxOf(view).Visibility);
    });

    [Fact]
    public void ClickingAway_KeepsWhatWasTyped() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);
        fx.App.RenameNoteCommand.Execute(null);
        Ui.Settle();

        BoxOf(view).Text = "Typed";
        view.Editor.Focus();
        Ui.Settle();

        Assert.Equal("Typed", note.CustomTitle);
        Assert.False(note.IsEditingTitle);
    });

    [Fact]
    public void ATitleEditedInTheWindow_IsSavedWithTheLayout() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var note = fx.App.CurrentWorkspace.Notes[0];
        note.BeginTitleEdit();
        note.EditTitle = "Saved name";
        note.CommitTitleEdit();

        fx.Repo.Save(SnapshotMapper.ToSnapshot(fx.App));
        var reloaded = new DocumentRepository(fx.Repo.Root).Load().Workspaces.Single().Notes.Single();

        Assert.Equal("Saved name", reloaded.CustomTitle);
    });
}
