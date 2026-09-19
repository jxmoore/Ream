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

    [ObservableProperty]
    private string _title = "";

    /// <summary>Share of the row's width this column takes (a preset like 1/2, or any dragged-to value).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WidthLabel))]
    private double _widthFraction = WidthPresets.Default;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isFocused;

    /// <summary>True while the column's edge is being dragged, so width changes follow the pointer instead of animating.</summary>
    [ObservableProperty]
    private bool _isResizing;

    public string WidthLabel => WidthPresets.Label(WidthFraction);

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
