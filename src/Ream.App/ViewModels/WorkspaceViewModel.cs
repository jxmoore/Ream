using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ream.App.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject
{
    public WorkspaceViewModel(string? name = null) : this(Guid.NewGuid(), name, null)
    {
    }

    public WorkspaceViewModel(Guid id, string? name, string? folderName)
    {
        Id = id;
        FolderName = folderName ?? $"ws-{id:N}"[..11];
        _name = name;
        Notes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public Guid Id { get; }

    /// <summary>Stable on-disk folder; never derived from the (editable) display name.</summary>
    public string FolderName { get; }

    public ObservableCollection<NoteViewModel> Notes { get; } = [];

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private int _focusedIndex;

    public bool IsEmpty => Notes.Count == 0;

    public NoteViewModel? FocusedNote =>
        FocusedIndex >= 0 && FocusedIndex < Notes.Count ? Notes[FocusedIndex] : null;

    public void SetFocus(int index)
    {
        if (Notes.Count == 0) return;
        index = Math.Clamp(index, 0, Notes.Count - 1);

        if (index != FocusedIndex && FocusedNote is { IsFullscreen: true } current)
            current.IsFullscreen = false;

        FocusedIndex = index;
        UpdateFocusFlags();
    }

    public void FocusBy(int delta) => SetFocus(FocusedIndex + delta);

    /// <summary>Populates an empty workspace from storage, preserving each note's saved state.</summary>
    public void LoadNotes(IEnumerable<NoteViewModel> notes, Guid? focusedNoteId)
    {
        foreach (var note in notes)
        {
            note.Owner = this;
            Notes.Add(note);
        }

        int focused = Notes.ToList().FindIndex(n => n.Id == focusedNoteId);
        FocusedIndex = Math.Max(0, focused);
        UpdateFocusFlags();
    }

    public void InsertAfterFocus(NoteViewModel note)
    {
        if (FocusedNote is { IsFullscreen: true } current)
            current.IsFullscreen = false;

        int at = Notes.Count == 0 ? 0 : Math.Clamp(FocusedIndex, 0, Notes.Count - 1) + 1;
        note.Owner = this;
        Notes.Insert(at, note);
        FocusedIndex = at;
        UpdateFocusFlags();
    }

    public NoteViewModel? RemoveFocused()
    {
        if (FocusedNote is not { } note) return null;

        int removedAt = FocusedIndex;
        Notes.RemoveAt(removedAt);
        note.Owner = null;
        note.IsFullscreen = false;
        note.IsFocused = false;

        FocusedIndex = Math.Max(0, Math.Min(removedAt, Notes.Count - 1));
        UpdateFocusFlags();
        return note;
    }

    public void MoveFocused(int delta)
    {
        int to = FocusedIndex + delta;
        if (FocusedNote is null || to < 0 || to >= Notes.Count) return;

        Notes.Move(FocusedIndex, to);
        FocusedIndex = to;
        UpdateFocusFlags();
    }

    private void UpdateFocusFlags()
    {
        for (int i = 0; i < Notes.Count; i++)
            Notes[i].IsFocused = i == FocusedIndex;
    }
}
