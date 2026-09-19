using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ream.Core.Abstractions;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

public sealed partial class AppViewModel : ObservableObject
{
    private int _noteCounter;

    private readonly IAssetStore? _assets;

    public AppViewModel(AppConfig config, IEnumerable<WorkspaceViewModel> workspaces, int currentIndex = 0, IAssetStore? assets = null)
    {
        _config = config;
        _assets = assets;
        Workspaces = new ObservableCollection<WorkspaceViewModel>(workspaces);
        EnsureTrailingEmpty();
        _currentIndex = Math.Clamp(currentIndex, 0, Workspaces.Count - 1);
        _noteCounter = Workspaces.Sum(w => w.Notes.Count);
        RefreshWorkspaceState();
        Workspaces.CollectionChanged += (_, _) => RefreshWorkspaceState();

        Actions = new Dictionary<string, ICommand>
        {
            ["focusPrevNote"] = FocusPrevNoteCommand,
            ["focusNextNote"] = FocusNextNoteCommand,
            ["switchWorkspaceUp"] = SwitchWorkspaceUpCommand,
            ["switchWorkspaceDown"] = SwitchWorkspaceDownCommand,
            ["moveNoteLeft"] = MoveNoteLeftCommand,
            ["moveNoteRight"] = MoveNoteRightCommand,
            ["moveNoteToPrevWorkspace"] = MoveNoteToPrevWorkspaceCommand,
            ["moveNoteToNextWorkspace"] = MoveNoteToNextWorkspaceCommand,
            ["cycleWidthPreset"] = CycleWidthPresetCommand,
            ["toggleFullscreen"] = ToggleFullscreenCommand,
            ["newNote"] = NewNoteCommand,
            ["closeNote"] = CloseNoteCommand,
            ["renameWorkspace"] = BeginRenameCommand,
        };
    }

    /// <summary>The settings in force. Replaced (not edited) when config.json is reloaded, so bindings refresh.</summary>
    [ObservableProperty]
    private AppConfig _config;

    /// <summary>Why the last reload of config.json was refused (the previous settings stay in force); null when fine.</summary>
    [ObservableProperty]
    private string? _configError;

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; }

    /// <summary>Keybinding action name (as used in config) to command.</summary>
    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentWorkspace))]
    private int _currentIndex;

    /// <summary>True while the workspace list shifts under the view, so the strip snaps instead of animating.</summary>
    [ObservableProperty]
    private bool _suppressAnimation;

    public WorkspaceViewModel CurrentWorkspace => Workspaces[CurrentIndex];

    partial void OnCurrentIndexChanged(int value) => RefreshWorkspaceState();

    /// <summary>Keeps each workspace's number and current flag in step with the list and the selection.</summary>
    private void RefreshWorkspaceState()
    {
        for (int i = 0; i < Workspaces.Count; i++)
        {
            Workspaces[i].Number = i + 1;
            Workspaces[i].IsCurrent = i == CurrentIndex;
        }
    }

    /// <summary>Raised when the focused note's editor should take keyboard focus (after focus or workspace moves).</summary>
    public event Action? FocusEditorRequested;

    public void RequestEditorFocus() => FocusEditorRequested?.Invoke();

    public void SwitchWorkspace(int delta)
    {
        int target = Math.Clamp(CurrentIndex + delta, 0, Workspaces.Count - 1);
        if (target == CurrentIndex) return;

        CurrentIndex = target;
        RequestEditorFocus();
    }

    /// <summary>Writes every note's pending edits into its saved form. Call before taking a snapshot.</summary>
    public void FlushPendingContent()
    {
        foreach (var workspace in Workspaces)
            foreach (var note in workspace.Notes)
                note.FlushDocument();
    }

    /// <summary>Like niri: there is always exactly one empty workspace at the end.</summary>
    private void EnsureTrailingEmpty()
    {
        if (Workspaces.Count == 0 || !Workspaces[^1].IsEmpty)
            Workspaces.Add(new WorkspaceViewModel(null, _assets));
    }

    /// <summary>
    /// Removes empty, unnamed workspaces other than the current one and the trailing one (named
    /// workspaces stay, as in niri). Called once a switch has come to rest, so nothing visible
    /// disappears mid-animation.
    /// </summary>
    [RelayCommand]
    private void PruneEmptyWorkspaces()
    {
        SuppressAnimation = true;
        try
        {
            for (int i = Workspaces.Count - 2; i >= 0; i--)
            {
                if (!Workspaces[i].IsEmpty || i == CurrentIndex || Workspaces[i].Name is not null) continue;
                Workspaces.RemoveAt(i);
                if (i < CurrentIndex) CurrentIndex--;
            }
        }
        finally
        {
            SuppressAnimation = false;
        }
    }

    /// <summary>Moves note focus along the current row and puts the keyboard cursor in that note.</summary>
    public void FocusNoteBy(int delta)
    {
        if (delta == 0) return;

        CurrentWorkspace.FocusBy(delta);
        RequestEditorFocus();
    }

    [RelayCommand] private void FocusPrevNote() => FocusNoteBy(-1);
    [RelayCommand] private void FocusNextNote() => FocusNoteBy(1);

    [RelayCommand]
    private void SelectWorkspace(WorkspaceViewModel? workspace)
    {
        int index = workspace is null ? -1 : Workspaces.IndexOf(workspace);
        if (index >= 0) SwitchWorkspace(index - CurrentIndex);
    }

    /// <summary>Starts renaming the given workspace (the current one when none is given).</summary>
    [RelayCommand]
    private void BeginRename(WorkspaceViewModel? workspace) => (workspace ?? CurrentWorkspace).BeginRename();

    [RelayCommand]
    private void CommitRename(WorkspaceViewModel? workspace)
    {
        workspace?.CommitRename();
        RequestEditorFocus();
    }

    [RelayCommand]
    private void CancelRename(WorkspaceViewModel? workspace)
    {
        workspace?.CancelRename();
        RequestEditorFocus();
    }

    [RelayCommand] private void SwitchWorkspaceUp() => SwitchWorkspace(-1);
    [RelayCommand] private void SwitchWorkspaceDown() => SwitchWorkspace(1);

    [RelayCommand]
    private void MoveNoteLeft()
    {
        CurrentWorkspace.MoveFocused(-1);
        RequestEditorFocus();
    }

    [RelayCommand]
    private void MoveNoteRight()
    {
        CurrentWorkspace.MoveFocused(1);
        RequestEditorFocus();
    }

    [RelayCommand] private void MoveNoteToPrevWorkspace() => MoveFocusedNoteToWorkspace(-1);
    [RelayCommand] private void MoveNoteToNextWorkspace() => MoveFocusedNoteToWorkspace(1);

    [RelayCommand]
    private void CycleWidthPreset()
    {
        if (CurrentWorkspace.FocusedNote is { } note)
            note.WidthFraction = WidthPresets.Next(note.WidthFraction);
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        if (CurrentWorkspace.FocusedNote is { } note)
            note.IsFullscreen = !note.IsFullscreen;
    }

    [RelayCommand]
    private void NewNote()
    {
        _noteCounter++;
        CurrentWorkspace.InsertAfterFocus(new NoteViewModel { Title = $"Untitled {_noteCounter}" });
        EnsureTrailingEmpty();
        RequestEditorFocus();
    }

    [RelayCommand]
    private void CloseNote()
    {
        CurrentWorkspace.RemoveFocused();
        EnsureTrailingEmpty();
        RequestEditorFocus();
    }

    private void MoveFocusedNoteToWorkspace(int delta)
    {
        int target = CurrentIndex + delta;
        if (target < 0 || target >= Workspaces.Count) return;

        if (CurrentWorkspace.RemoveFocused() is not { } note) return;

        Workspaces[target].InsertAfterFocus(note);
        EnsureTrailingEmpty();
        CurrentIndex = target;
        RequestEditorFocus();
    }
}
