using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ream.Core.Models;
using Ream.Persistence.NoteFormat;

namespace Ream.App.ViewModels;

public sealed partial class NoteViewModel : ObservableObject
{
    /// <summary>Raised (as a property change) whenever the note's text or formatting is edited.</summary>
    public const string ContentChangedProperty = "Content";

    private FlowDocument? _document;
    private bool _contentDirty;

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The saved form of the note (.reamnote XML). Refreshed from the live document by <see cref="FlushDocument"/>.</summary>
    [ObservableProperty]
    private string _body = "";

    /// <summary>The automatic title: the note's first line. What the header shows is <see cref="DisplayTitle"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title = "";

    /// <summary>A title the user chose; null means "use the first line".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _customTitle;

    public string DisplayTitle => string.IsNullOrWhiteSpace(CustomTitle) ? Title : CustomTitle;

    public const int MaxCustomTitleLength = 80;

    /// <summary>True while the header shows a text box for renaming.</summary>
    [ObservableProperty]
    private bool _isEditingTitle;

    [ObservableProperty]
    private string _editTitle = "";

    public void BeginTitleEdit()
    {
        if (IsEditingTitle) return;

        EditTitle = DisplayTitle;
        IsEditingTitle = true;
    }

    /// <summary>Applies the typed title (blank goes back to the first line). Does nothing unless an edit is in progress.</summary>
    public void CommitTitleEdit()
    {
        if (!IsEditingTitle) return;
        IsEditingTitle = false;

        string typed = EditTitle.Trim();
        if (typed.Length == 0) CustomTitle = null;
        else if (typed != DisplayTitle) CustomTitle = typed[..Math.Min(typed.Length, MaxCustomTitleLength)];

        // A note somebody has named is theirs, not a draft.
        if (!string.IsNullOrWhiteSpace(CustomTitle)) IsDraft = false;
    }

    public void CancelTitleEdit() => IsEditingTitle = false;

    /// <summary>Share of the row's width this column takes (a preset like 1/2, or any dragged-to value).</summary>
    [ObservableProperty]
    private double _widthFraction = WidthPresets.Default;

    /// <summary>
    /// A note made by navigating past the end of a row (or by New). It isn't saved and disappears if left
    /// blank; the first time it has content it becomes an ordinary note.
    /// </summary>
    [ObservableProperty]
    private bool _isDraft;

    [ObservableProperty]
    private bool _isFullscreen;

    partial void OnIsFullscreenChanged(bool value)
    {
        // Read Mode's read-only lock never outlives its fullscreen - whatever cleared one clears both.
        if (!value) IsReadOnly = false;
    }

    /// <summary>Read Mode's "no editing" half - Read Mode always sets this alongside <see cref="IsFullscreen"/>; Alt+F11's plain fullscreen never does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveReadOnly))]
    private bool _isReadOnly;

    /// <summary>Draft view: images are hidden in the editor (not removed - still saved, still there when this is off).</summary>
    [ObservableProperty]
    private bool _hideImages;

    /// <summary>Outline view: the editor shows a generated, read-only summary of just the heading paragraphs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveReadOnly))]
    private bool _isOutlineView;

    /// <summary>What the editor actually binds its own IsReadOnly to: Read Mode's lock, or Outline view's (which is always read-only, being a generated summary).</summary>
    public bool EffectiveReadOnly => IsReadOnly || IsOutlineView;

    /// <summary>Show group's Ruler: a draggable left-indent marker shown above this note's editor.</summary>
    [ObservableProperty]
    private bool _showRuler;

    /// <summary>
    /// Window group's One Page: true for every note but the focused one while <see cref="AppViewModel.OnePageMode"/>
    /// is on (<see cref="AppViewModel.ApplyOnePageVisibility"/> keeps this in step with focus). Unlike Read Mode's
    /// fullscreen, the note that stays visible keeps its own normal size - the others are hidden, not enlarged.
    /// </summary>
    [ObservableProperty]
    private bool _isHiddenByOnePage;

    [ObservableProperty]
    private bool _isFocused;

    /// <summary>True while the column's edge is being dragged, so width changes follow the pointer instead of animating.</summary>
    [ObservableProperty]
    private bool _isResizing;

    /// <summary>Raised (with e.g. "55%") when the width was just changed on purpose, so the view can flash it briefly.</summary>
    public event Action<string>? SizeToastRequested;

    public void ShowSizeToast() => SizeToastRequested?.Invoke(WidthPresets.Percent(WidthFraction));
    internal WorkspaceViewModel? Owner { get; set; }

    /// <summary>Raised when the app wants this note's editor to take keyboard focus.</summary>
    public event Action? EditorFocusRequested;

    public void RequestEditorFocus() => EditorFocusRequested?.Invoke();

    /// <summary>Raised (with the real paragraph to land on) when the Navigation Pane's heading list is clicked.</summary>
    public event Action<Paragraph>? CaretMoveRequested;

    public void RequestCaretMove(Paragraph paragraph) => CaretMoveRequested?.Invoke(paragraph);

    /// <summary>
    /// The live FlowDocument an editor is showing for this note (the same object <see cref="OpenDocument"/> handed
    /// back - editing happens in place, so this always reflects what's on screen), or null before that has ever
    /// happened. The Navigation Pane's heading list reads this directly rather than going through the view.
    /// </summary>
    public FlowDocument? LiveDocument => _document;

    /// <summary>
    /// Builds the document an editor shows. Each editor gets its own document parsed from the saved
    /// form, so a recreated view never tries to share a document with the one it replaces.
    /// </summary>
    public FlowDocument OpenDocument()
    {
        FlushDocument();

        var document = NoteDocumentSerializer.Deserialize(Body, LoadImage);
        _document = document;
        _contentDirty = false;
        return document;
    }

    public void NotifyContentChanged()
    {
        _contentDirty = true;
        OnPropertyChanged(ContentChangedProperty);

        // The draft cue (dashed outline, "Draft" pill) goes away the moment there is something to keep.
        if (IsDraft && DocumentHasContent()) IsDraft = false;
    }

    /// <summary>True when the live document holds any text or a picture. Cheap: a draft is small.</summary>
    private bool DocumentHasContent()
    {
        if (_document is null) return false;
        if (!string.IsNullOrWhiteSpace(new TextRange(_document.ContentStart, _document.ContentEnd).Text)) return true;

        for (var p = _document.ContentStart; p is not null && p.CompareTo(_document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer) return true;
        }
        return false;
    }

    /// <summary>Writes pending edits into <see cref="Body"/> and refreshes the title from the text.</summary>
    public void FlushDocument()
    {
        if (!_contentDirty || _document is null) return;

        try
        {
            Body = NoteDocumentSerializer.Serialize(_document, Id, SaveImage);

            string text = new TextRange(_document.ContentStart, _document.ContentEnd).Text;
            if (NoteContent.TryDeriveTitle(text) is { } title) Title = title;

            _contentDirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Stay dirty so the next save retries; a failed image write must not take the app down.
            Debug.WriteLine($"Couldn't serialize note {Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// True for a draft with nothing in it yet. Brings the saved form up to date first, and a draft that
    /// has gained content stops being a draft.
    /// </summary>
    public bool IsBlankDraft()
    {
        FlushDocument();
        if (!IsDraft) return false;

        // A title the user typed makes it theirs, even before there is any text.
        if (NoteContent.IsBlank(Body) && string.IsNullOrWhiteSpace(CustomTitle))
            return true;

        IsDraft = false;
        return false;
    }

    /// <summary>Stores pasted image bytes next to the note and returns the asset name.</summary>
    public string SaveImage(byte[] png)
    {
        if (Owner?.Assets is not { } assets)
            throw new InvalidOperationException("This note isn't in a workspace with storage.");
        return assets.SaveAsset(Owner.FolderName, Id, png);
    }

    private ImageSource? LoadImage(string name)
    {
        if (Owner?.Assets is not { } assets) return null;
        return assets.GetAssetPath(Owner.FolderName, Id, name) is { } path ? NoteImage.LoadFile(path) : null;
    }

    [RelayCommand]
    private void Focus() => Owner?.SetFocus(Owner.Notes.IndexOf(this));
}
