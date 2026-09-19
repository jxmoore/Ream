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

        // An empty workspace on each side; currentIndex counts within the list that was passed in.
        int shift = 0;
        if (NeedsLeadingEmpty)
        {
            Workspaces.Insert(0, new WorkspaceViewModel(null, _assets));
            shift = 1;
        }
        if (NeedsTrailingEmpty) Workspaces.Add(new WorkspaceViewModel(null, _assets));

        _currentIndex = Math.Clamp(currentIndex + shift, 0, Workspaces.Count - 1);
        _visited = Workspaces[_currentIndex];
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
            ["sizeUp"] = SizeUpCommand,
            ["sizeDown"] = SizeDownCommand,
            ["toggleFullscreen"] = ToggleFullscreenCommand,
            ["toggleAppFullscreen"] = ToggleAppFullscreenCommand,
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

    // The workspace that was current last time we looked. Compared by identity, so the index shifting
    // under a list edit isn't mistaken for leaving a workspace.
    private WorkspaceViewModel _visited;

    partial void OnCurrentIndexChanged(int value)
    {
        var arrived = Workspaces[value];
        var left = _visited;
        _visited = arrived;

        // A draft nobody typed into doesn't outlive the visit that made it.
        if (!ReferenceEquals(left, arrived) && Workspaces.Contains(left))
            left.DiscardBlankDrafts(keepFocused: false);

        RefreshWorkspaceState();
    }

    /// <summary>
    /// Keeps each workspace's number, edge and current flags in step with the list and the selection.
    /// The first and last workspace are the always-empty edges; the ones between are numbered from 1.
    /// </summary>
    private void RefreshWorkspaceState()
    {
        for (int i = 0; i < Workspaces.Count; i++)
        {
            Workspaces[i].IsEdge = i == 0 || i == Workspaces.Count - 1;
            Workspaces[i].Number = i;
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

    private bool NeedsLeadingEmpty => Workspaces.Count == 0 || !Workspaces[0].IsEmpty;

    // With a single workspace that is the leading empty one, so a second is needed to be the trailing one.
    private bool NeedsTrailingEmpty => Workspaces.Count < 2 || !Workspaces[^1].IsEmpty;

    /// <summary>
    /// Like niri, but on both sides: there is always an empty workspace above the first and below the last.
    /// A new one at the top pushes everything down, so the strip snaps rather than sliding.
    /// </summary>
    private void EnsureEdgeWorkspaces()
    {
        if (NeedsLeadingEmpty)
        {
            SuppressAnimation = true;
            try
            {
                Workspaces.Insert(0, new WorkspaceViewModel(null, _assets));
                CurrentIndex++;
            }
            finally
            {
                SuppressAnimation = false;
            }
        }

        if (NeedsTrailingEmpty)
            Workspaces.Add(new WorkspaceViewModel(null, _assets));
    }

    /// <summary>
    /// Removes empty, unnamed workspaces between the two edges, other than the current one (named
    /// workspaces stay, as in niri). Called once a switch has come to rest, so nothing visible
    /// disappears mid-animation.
    /// </summary>
    [RelayCommand]
    private void PruneEmptyWorkspaces()
    {
        SuppressAnimation = true;
        try
        {
            for (int i = Workspaces.Count - 2; i >= 1; i--)
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

    [RelayCommand] private void FocusPrevNote() => StepNote(-1);
    [RelayCommand] private void FocusNextNote() => StepNote(1);

    /// <summary>
    /// The Alt+Left / Alt+Right behavior: move to the neighbouring note, and past the end of the row open a
    /// blank draft there. Never stacks drafts. (Scroll wheels use <see cref="FocusNoteBy"/>, which just stops.)
    /// </summary>
    private void StepNote(int direction)
    {
        var workspace = CurrentWorkspace;
        int next = workspace.FocusedIndex + direction;

        if (workspace.Notes.Count > 0 && next >= 0 && next < workspace.Notes.Count)
        {
            FocusNoteBy(direction);
            return;
        }

        OpenDraft(before: direction < 0);
    }

    /// <summary>Adds a blank draft beside the focused note - unless the focused note already is one.</summary>
    private void OpenDraft(bool before)
    {
        var workspace = CurrentWorkspace;
        if (workspace.FocusedNote?.IsBlankDraft() != true)
        {
            _noteCounter++;
            var draft = new NoteViewModel { Title = $"Untitled {_noteCounter}", IsDraft = true };
            if (before) workspace.InsertBeforeFocus(draft);
            else workspace.InsertAfterFocus(draft);
            EnsureEdgeWorkspaces();
        }

        RequestEditorFocus();
    }

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

    [RelayCommand] private void SizeUp() => Nudge(1);
    [RelayCommand] private void SizeDown() => Nudge(-1);

    private void Nudge(int direction)
    {
        if (CurrentWorkspace.FocusedNote is { } note)
            note.WidthFraction = WidthPresets.Nudge(note.WidthFraction, direction);
    }

    /// <summary>Raised when the whole window should go fullscreen or come back (the window owns that, not us).</summary>
    public event Action? AppFullscreenToggleRequested;

    [RelayCommand]
    private void ToggleAppFullscreen() => AppFullscreenToggleRequested?.Invoke();

    [RelayCommand]
    private void ToggleFullscreen()
    {
        if (CurrentWorkspace.FocusedNote is { } note)
            note.IsFullscreen = !note.IsFullscreen;
    }

    [RelayCommand]
    private void NewNote() => OpenDraft(before: false);

    [RelayCommand]
    private void CloseNote()
    {
        CurrentWorkspace.RemoveFocused();
        EnsureEdgeWorkspaces();
        RequestEditorFocus();
    }

    private void MoveFocusedNoteToWorkspace(int delta)
    {
        int target = CurrentIndex + delta;
        if (target < 0 || target >= Workspaces.Count) return;

        if (CurrentWorkspace.RemoveFocused() is not { } note) return;

        var destination = Workspaces[target];
        destination.InsertAfterFocus(note);
        EnsureEdgeWorkspaces();
        CurrentIndex = Workspaces.IndexOf(destination);
        RequestEditorFocus();
    }
}
