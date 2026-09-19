using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ream.Core.Abstractions;

namespace Ream.App.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject
{
    public WorkspaceViewModel(string? name = null, IAssetStore? assets = null) : this(Guid.NewGuid(), name, null, assets)
    {
    }

    public WorkspaceViewModel(Guid id, string? name, string? folderName, IAssetStore? assets = null)
    {
        Id = id;
        Assets = assets;
        FolderName = folderName ?? $"ws-{id:N}"[..11];
        _name = name;
        Notes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public Guid Id { get; }

    /// <summary>Where this workspace's notes keep their images.</summary>
    public IAssetStore? Assets { get; }

    /// <summary>Stable on-disk folder; never derived from the (editable) display name.</summary>
    public string FolderName { get; }

    public ObservableCollection<NoteViewModel> Notes { get; } = [];

    private const int MaxNameLength = 40;

    /// <summary>The user's name for this workspace; null means unnamed (shown by its number).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string? _name;

    /// <summary>1-based position in the workspace list, kept current by the app.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private int _number;

    /// <summary>True for the first and last workspace, which are never numbered (an unnamed one shows "+").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private bool _isEdge;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _editName = "";

    [ObservableProperty]
    private int _focusedIndex;

    public string DisplayLabel => Name ?? (IsEdge ? "+" : Number.ToString());

    public void BeginRename()
    {
        EditName = Name ?? "";
        IsRenaming = true;
    }

    /// <summary>Applies the typed name; blank clears it. Does nothing unless a rename is in progress.</summary>
    public void CommitRename()
    {
        if (!IsRenaming) return;

        string trimmed = EditName.Trim();
        Name = trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, MaxNameLength)];
        IsRenaming = false;
    }

    public void CancelRename() => IsRenaming = false;

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
        DiscardBlankDrafts(keepFocused: true);
    }

    public void FocusBy(int delta) => SetFocus(FocusedIndex + delta);

    /// <summary>
    /// Removes drafts that are still blank - the ones left behind when focus moves on. The focused note is
    /// spared unless <paramref name="keepFocused"/> is false (the workspace itself is being left).
    /// </summary>
    public void DiscardBlankDrafts(bool keepFocused)
    {
        var focused = FocusedNote;
        var doomed = Notes.Where(n => n.IsDraft && !(keepFocused && n == focused) && n.IsBlankDraft()).ToList();
        if (doomed.Count == 0) return;

        foreach (var note in doomed)
        {
            Notes.Remove(note);
            note.Owner = null;
            note.IsFullscreen = false;
            note.IsFocused = false;
        }

        int kept = focused is null ? -1 : Notes.IndexOf(focused);
        FocusedIndex = kept >= 0 ? kept : Math.Clamp(FocusedIndex, 0, Math.Max(0, Notes.Count - 1));
        UpdateFocusFlags();
    }

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

    /// <summary>Inserts the note to the left of the focused one and focuses it.</summary>
    public void InsertBeforeFocus(NoteViewModel note)
    {
        if (FocusedNote is { IsFullscreen: true } current)
            current.IsFullscreen = false;

        int at = Notes.Count == 0 ? 0 : Math.Clamp(FocusedIndex, 0, Notes.Count - 1);
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
