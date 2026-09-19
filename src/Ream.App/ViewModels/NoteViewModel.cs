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
