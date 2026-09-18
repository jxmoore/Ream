using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ream.Core.Models;

namespace Ream.App.ViewModels;

public sealed partial class AppViewModel : ObservableObject
{
    private int _noteCounter;

    public AppViewModel(AppConfig config, IEnumerable<WorkspaceViewModel> workspaces)
    {
        Config = config;
        Workspaces = new ObservableCollection<WorkspaceViewModel>(workspaces);
        EnsureTrailingEmpty();
        _noteCounter = Workspaces.Sum(w => w.Notes.Count);
        Workspaces.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IndicatorText));

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
        };
    }

    public AppConfig Config { get; }

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; }

    /// <summary>Keybinding action name (as used in config) to command.</summary>
    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentWorkspace), nameof(IndicatorText))]
    private int _currentIndex;

    /// <summary>True while the workspace list shifts under the view, so the strip snaps instead of animating.</summary>
    [ObservableProperty]
    private bool _suppressAnimation;

    public WorkspaceViewModel CurrentWorkspace => Workspaces[CurrentIndex];

    public string IndicatorText =>
        $"{CurrentWorkspace.Name ?? $"Workspace {CurrentIndex + 1}"}   {CurrentIndex + 1}/{Workspaces.Count}";

    public void SwitchWorkspace(int delta)
    {
        int target = Math.Clamp(CurrentIndex + delta, 0, Workspaces.Count - 1);
        if (target != CurrentIndex) CurrentIndex = target;
    }

    /// <summary>Like niri: there is always exactly one empty workspace at the end.</summary>
    private void EnsureTrailingEmpty()
    {
        if (Workspaces.Count == 0 || !Workspaces[^1].IsEmpty)
            Workspaces.Add(new WorkspaceViewModel());
    }

    /// <summary>
    /// Removes empty workspaces other than the current one and the trailing one. Called once a
    /// switch has come to rest, so nothing visible disappears mid-animation.
    /// </summary>
    [RelayCommand]
    private void PruneEmptyWorkspaces()
    {
        SuppressAnimation = true;
        try
        {
            for (int i = Workspaces.Count - 2; i >= 0; i--)
            {
                if (!Workspaces[i].IsEmpty || i == CurrentIndex) continue;
                Workspaces.RemoveAt(i);
                if (i < CurrentIndex) CurrentIndex--;
            }
        }
        finally
        {
            SuppressAnimation = false;
        }
    }

    [RelayCommand] private void FocusPrevNote() => CurrentWorkspace.FocusBy(-1);
    [RelayCommand] private void FocusNextNote() => CurrentWorkspace.FocusBy(1);
    [RelayCommand] private void SwitchWorkspaceUp() => SwitchWorkspace(-1);
    [RelayCommand] private void SwitchWorkspaceDown() => SwitchWorkspace(1);
    [RelayCommand] private void MoveNoteLeft() => CurrentWorkspace.MoveFocused(-1);
    [RelayCommand] private void MoveNoteRight() => CurrentWorkspace.MoveFocused(1);
    [RelayCommand] private void MoveNoteToPrevWorkspace() => MoveFocusedNoteToWorkspace(-1);
    [RelayCommand] private void MoveNoteToNextWorkspace() => MoveFocusedNoteToWorkspace(1);

    [RelayCommand]
    private void CycleWidthPreset()
    {
        if (CurrentWorkspace.FocusedNote is { } note)
            note.WidthPreset = note.WidthPreset.Next();
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
    }

    [RelayCommand]
    private void CloseNote()
    {
        CurrentWorkspace.RemoveFocused();
        EnsureTrailingEmpty();
    }

    private void MoveFocusedNoteToWorkspace(int delta)
    {
        int target = CurrentIndex + delta;
        if (target < 0 || target >= Workspaces.Count) return;

        if (CurrentWorkspace.RemoveFocused() is not { } note) return;

        Workspaces[target].InsertAfterFocus(note);
        EnsureTrailingEmpty();
        CurrentIndex = target;
    }
}
