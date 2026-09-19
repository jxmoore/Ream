using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ream.App.Controls;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Layout;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>The real main window, off-screen and never activated, over a real repository in a temp folder.</summary>
internal sealed class WindowFixture : IDisposable
{
    public WindowFixture(params (string? Name, int Notes)[] workspaces)
        : this(new AppConfig(), workspaces)
    {
    }

    public WindowFixture(AppConfig config, params (string? Name, int Notes)[] workspaces)
    {
        Dir = new TempDir();
        Repo = new DocumentRepository(Dir.Combine("Docs"));

        var built = workspaces.Select(spec =>
        {
            var workspace = new WorkspaceViewModel(spec.Name, Repo);
            workspace.LoadNotes(Enumerable.Range(0, spec.Notes).Select(i => new NoteViewModel { Title = $"note {i}" }), null);
            return workspace;
        }).ToList();

        App = new AppViewModel(config, built, 0, Repo);
        Window = new Ream.App.MainWindow(App)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        Window.Show();
        Ui.Settle();
        Ui.Settle();
    }

    public TempDir Dir { get; }
    public DocumentRepository Repo { get; }
    public AppViewModel App { get; }
    public Ream.App.MainWindow Window { get; }

    public WorkspaceTabs Tabs => Ui.Descendants<WorkspaceTabs>(Window).Single();

    public IEnumerable<NoteColumnView> Columns => Ui.Descendants<NoteColumnView>(Window);

    public NoteColumnView ColumnOf(NoteViewModel note) => Columns.Single(c => ReferenceEquals(c.DataContext, note));

    public void Dispose()
    {
        Window.Close();
        Dir.Dispose();
    }
}

public class MainWindowIntegrationTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    // ----- Resizing by dragging -----

    private static void DragStart(NoteColumnView view) =>
        view.ResizeHandle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });

    private static void DragBy(NoteColumnView view, double dx) =>
        view.ResizeHandle.RaiseEvent(new DragDeltaEventArgs(dx, 0) { RoutedEvent = Thumb.DragDeltaEvent });

    private static void DragEnd(NoteColumnView view) =>
        view.ResizeHandle.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });

    private static NoteRowPanel RowOf(NoteColumnView view) => Ui.Ancestor<NoteRowPanel>(view)!;

    [Fact]
    public void DraggingTheEdge_ResizesTheColumn_ByTheDraggedDistance() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);
        var row = RowOf(view);
        double startWidth = view.ActualWidth;

        DragStart(view);
        DragBy(view, 100);

        double expected = RowLayout.FractionForWidth(startWidth + 100, row.ActualWidth, row.Config.Layout.GapPx);
        Assert.Equal(expected, note.WidthFraction, 4);
        Assert.True(note.WidthFraction > 0.5);
        Assert.True(note.IsResizing);

        DragEnd(view);
        Assert.False(note.IsResizing);
    });

    [Fact]
    public void WhileDragging_TheColumnFollowsThePointerInsteadOfAnimating() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);

        DragStart(view);
        DragBy(view, 120);
        Ui.Settle();

        // An animation would still be on its way (150 ms); following the pointer is already there.
        var container = VisualTreeHelper.GetParent(view);
        Assert.Equal(note.WidthFraction, NoteRowPanel.GetActualFraction(container), 6);
        DragEnd(view);
    });

    [Fact]
    public void SeveralDragEventsBeforeALayoutPass_LoseNoMovement() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);
        var row = RowOf(view);
        double startWidth = view.ActualWidth;

        DragStart(view);
        DragBy(view, 30);
        DragBy(view, 30);
        DragBy(view, 30);

        double expected = RowLayout.FractionForWidth(startWidth + 90, row.ActualWidth, row.Config.Layout.GapPx);
        Assert.Equal(expected, note.WidthFraction, 4);
        DragEnd(view);
    });

    [Fact]
    public void DraggingFarPastTheLimits_StopsAtTheMinimumAndMaximum() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);

        DragStart(view);
        DragBy(view, -50_000);
        Assert.Equal(WidthPresets.Min, note.WidthFraction, 6);

        DragBy(view, 100_000);
        Assert.Equal(WidthPresets.Max, note.WidthFraction, 6);
        DragEnd(view);
    });

    [Fact]
    public void ADragThatOvershotTheLimit_ComesBackAsSoonAsThePointerDoes() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);

        DragStart(view);
        DragBy(view, 50_000);
        DragBy(view, -60);

        Assert.True(note.WidthFraction < WidthPresets.Max);
        DragEnd(view);
    });

    [Fact]
    public void AFullscreenNote_IgnoresResizeDrags() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);
        note.IsFullscreen = true;

        DragStart(view);
        DragBy(view, 200);
        DragEnd(view);

        Assert.Equal(0.5, note.WidthFraction);
    });

    [Fact]
    public void ADraggedWidth_ShowsInTheLabel_AndIsSaved() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);

        DragStart(view);
        DragBy(view, 77);
        DragEnd(view);

        Assert.EndsWith("%", note.WidthLabel);
        Assert.Equal(note.WidthFraction, SnapshotMapper.ToSnapshot(fx.App).Workspaces[0].Notes[0].WidthFraction, 9);
    });

    [Fact]
    public void ADraggedWidth_SurvivesSaveAndReload() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var note = fx.App.CurrentWorkspace.Notes[0];
        var view = fx.ColumnOf(note);
        DragStart(view);
        DragBy(view, -60);
        DragEnd(view);

        fx.Repo.Save(SnapshotMapper.ToSnapshot(fx.App));
        var reloaded = new DocumentRepository(fx.Repo.Root).Load();

        Assert.Equal(note.WidthFraction, reloaded.Workspaces[0].Notes[0].WidthFraction, 3);
    });

    [Fact]
    public void ResizingAColumn_LeavesItsNeighboursWidthsAlone() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 3));
        var others = fx.App.CurrentWorkspace.Notes.Skip(1).Select(n => n.WidthFraction).ToList();
        var view = fx.ColumnOf(fx.App.CurrentWorkspace.Notes[0]);

        DragStart(view);
        DragBy(view, 150);
        DragEnd(view);

        Assert.Equal(others, fx.App.CurrentWorkspace.Notes.Skip(1).Select(n => n.WidthFraction));
    });

    // ----- Horizontal (tilt) wheel -----

    private static void Tilt(Window window, int delta)
    {
        var handle = new WindowInteropHelper(window).Handle;
        long wParam = (long)(ushort)(short)delta << 16;
        SendMessage(handle, MouseTilt(), new IntPtr(wParam), IntPtr.Zero);
    }

    private static int MouseTilt() => Ream.Core.Utilities.MouseTilt.WmMouseHWheel;

    [Fact]
    public void TiltingTheWheel_MovesFocusAlongTheRow() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 3));
        var workspace = fx.App.CurrentWorkspace;

        Tilt(fx.Window, 120);
        Assert.Equal(1, workspace.FocusedIndex);

        Tilt(fx.Window, 120);
        Assert.Equal(2, workspace.FocusedIndex);

        Tilt(fx.Window, -120);
        Assert.Equal(1, workspace.FocusedIndex);
    });

    [Fact]
    public void TiltingPastTheEnds_StaysPut() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var workspace = fx.App.CurrentWorkspace;

        Tilt(fx.Window, -120);
        Assert.Equal(0, workspace.FocusedIndex);

        Tilt(fx.Window, 120);
        Tilt(fx.Window, 120);
        Tilt(fx.Window, 120);
        Assert.Equal(1, workspace.FocusedIndex);
    });

    [Fact]
    public void SmallTiltSteps_AddUpToOneMove() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 3));

        Tilt(fx.Window, 40);
        Tilt(fx.Window, 40);
        Assert.Equal(0, fx.App.CurrentWorkspace.FocusedIndex);

        Tilt(fx.Window, 40);
        Assert.Equal(1, fx.App.CurrentWorkspace.FocusedIndex);
    });

    [Fact]
    public void Tilting_AsksForTheEditorToTakeFocus() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        int requests = 0;
        fx.App.FocusEditorRequested += () => requests++;

        Tilt(fx.Window, 120);

        Assert.Equal(1, requests);
    });

    // ----- Workspace strip -----

    [Fact]
    public void TheStrip_ShowsOneChipPerWorkspace_WithNamesAndNumbers() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1), (null, 1));

        Assert.Equal(3, fx.Tabs.Tabs.Items.Count);
        var labels = Ui.Descendants<TextBlock>(fx.Tabs).Where(t => t.Name == "Label").Select(t => t.Text).ToList();
        Assert.Equal(["Work", "2", "3"], labels);
    });

    [Fact]
    public void TheStrip_FollowsSwitchesAndRenames() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1), (null, 1));

        fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[1]);
        fx.App.Workspaces[1].Name = "Home";
        Ui.Settle();

        var labels = Ui.Descendants<TextBlock>(fx.Tabs).Where(t => t.Name == "Label").Select(t => t.Text).ToList();
        Assert.Equal(["Work", "Home", "3"], labels);
        Assert.Equal([false, true, false], fx.App.Workspaces.Select(w => w.IsCurrent));
    });

    [Fact]
    public void TheStrip_ShowsANewWorkspace_WhenOneIsAdded() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));
        fx.App.SelectWorkspaceCommand.Execute(fx.App.Workspaces[1]);

        fx.App.NewNoteCommand.Execute(null);
        Ui.Settle();

        Assert.Equal(3, fx.Tabs.Tabs.Items.Count);
    });

    private static TextBox VisibleNameBox(WindowFixture fx)
    {
        var boxes = Ui.Descendants<TextBox>(fx.Tabs).ToList();
        var visible = boxes.Where(box => box.Visibility == Visibility.Visible).ToList();
        Assert.True(
            visible.Count == 1,
            $"visible boxes: {visible.Count} of {boxes.Count}; " +
            $"workspaces: {string.Join(", ", fx.App.Workspaces.Select(w => $"[{w.DisplayLabel} renaming={w.IsRenaming} edit='{w.EditName}']"))}");
        return visible[0];
    }

    private static void Press(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    [Fact]
    public void Renaming_ShowsATextBox_AndEnterAppliesTheName() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));

        fx.App.BeginRenameCommand.Execute(null);
        Ui.Settle();
        var box = VisibleNameBox(fx);
        Assert.Equal("Work", box.Text);

        box.Text = "Projects";
        Press(box, Key.Enter);
        Ui.Settle();

        Assert.Equal("Projects", fx.App.Workspaces[0].Name);
        Assert.False(fx.App.Workspaces[0].IsRenaming);
        Assert.Empty(Ui.Descendants<TextBox>(fx.Tabs).Where(b => b.Visibility == Visibility.Visible));
    });

    [Fact]
    public void Escape_CancelsARename() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));
        fx.App.BeginRenameCommand.Execute(null);
        Ui.Settle();

        var box = VisibleNameBox(fx);
        box.Text = "Nope";
        Press(box, Key.Escape);
        Ui.Settle();

        Assert.Equal("Work", fx.App.Workspaces[0].Name);
        Assert.False(fx.App.Workspaces[0].IsRenaming);
    });

    [Fact]
    public void ARename_IsBoundToTheConfiguredShortcut() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));

        var binding = fx.Window.InputBindings.OfType<KeyBinding>()
            .Single(b => b.Key == Key.R && b.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift));

        Assert.Same(fx.App.BeginRenameCommand, binding.Command);
    });

    [Fact]
    public void ARenamedWorkspace_SurvivesSaveAndReload() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));
        fx.App.Workspaces[0].BeginRename();
        fx.App.Workspaces[0].EditName = "Renamed";
        fx.App.CommitRenameCommand.Execute(fx.App.Workspaces[0]);

        fx.Repo.Save(SnapshotMapper.ToSnapshot(fx.App));
        var reloaded = new DocumentRepository(fx.Repo.Root).Load();

        Assert.Equal("Renamed", reloaded.Workspaces[0].Name);
    });

    [Fact]
    public void ANamedEmptyWorkspace_SurvivesSaveAndReload() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("Work", 1));
        fx.App.Workspaces[^1].Name = "Someday";

        fx.Repo.Save(SnapshotMapper.ToSnapshot(fx.App));
        var reloaded = new DocumentRepository(fx.Repo.Root).Load();

        Assert.Equal(["Work", "Someday"], reloaded.Workspaces.Select(w => w.Name));
        Assert.Empty(reloaded.Workspaces[1].Notes);
    });
}
