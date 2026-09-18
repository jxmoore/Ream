using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Ream.App.ViewModels;
using Ream.Persistence.NoteFormat;

namespace Ream.App.Views;

public partial class NoteColumnView : UserControl
{
    private NoteViewModel? _note;
    private bool _subscribed;
    private bool _loadingDocument;

    public NoteColumnView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => OnUnloaded();

        Editor.TextChanged += OnTextChanged;
        Editor.SizeChanged += (_, _) => FitImages();
        Editor.GotKeyboardFocus += (_, _) => _note?.FocusCommand.Execute(null);
        DataObject.AddPastingHandler(Editor, OnPaste);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe();
        _note = e.NewValue as NoteViewModel;
        if (_note is null) return;

        _loadingDocument = true;
        try
        {
            // A FlowDocument doesn't inherit look-and-feel from its RichTextBox, so give it the editor's
            // defaults. They also become the baseline the saved file is diffed against.
            var document = _note.OpenDocument();
            document.FontFamily = Editor.FontFamily;
            document.FontSize = Editor.FontSize;
            document.Foreground = Editor.Foreground;
            Editor.Document = document;
        }
        finally
        {
            _loadingDocument = false;
        }

        FitImages();
        if (IsLoaded) Subscribe();
    }

    private void Subscribe()
    {
        if (_note is null || _subscribed) return;
        _note.EditorFocusRequested += OnEditorFocusRequested;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_note is null || !_subscribed) return;
        _note.EditorFocusRequested -= OnEditorFocusRequested;
        _subscribed = false;
    }

    private void OnUnloaded()
    {
        Unsubscribe();
        _note?.FlushDocument();
    }

    private void OnEditorFocusRequested()
    {
        if (Editor.IsLoaded)
        {
            Editor.Focus();
            return;
        }

        void FocusOnce(object? sender, RoutedEventArgs args)
        {
            Editor.Loaded -= FocusOnce;
            Editor.Focus();
        }
        Editor.Loaded += FocusOnce;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingDocument) return;
        _note?.NotifyContentChanged();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _note?.FocusCommand.Execute(null);

    // ----- Images -----

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (_note is null || !ClipboardImage.HasPastableImage(e.DataObject)) return;
        if (!ClipboardImage.TryGetPng(e.DataObject, out var png)) return;

        // Ours to handle: the editor's own paste would drop the picture or embed it unsaved.
        e.CancelCommand();
        InsertImage(png);
    }

    internal void InsertImage(byte[] png)
    {
        if (_note is null || NoteImage.FromPng(png) is not { } source) return;

        string name;
        try
        {
            name = _note.SaveImage(png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine($"Couldn't save pasted image: {ex.Message}");
            SystemSounds.Beep.Play();
            return;
        }

        var image = NoteImage.Create(source, name);

        Editor.BeginChange();
        try
        {
            if (!Editor.Selection.IsEmpty) Editor.Selection.Text = string.Empty;

            var at = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var container = new InlineUIContainer(image, at) { BaselineAlignment = BaselineAlignment.Bottom };
            Editor.CaretPosition = container.ElementEnd;
        }
        finally
        {
            Editor.EndChange();
        }

        FitImages();
    }

    /// <summary>Keeps pictures no wider than the column; the saved size is untouched.</summary>
    private void FitImages()
    {
        if (Editor.Document is not { } document) return;

        double max = Math.Max(60, Editor.ActualWidth - Editor.Padding.Left - Editor.Padding.Right - 24);
        for (var p = document.ContentStart;
             p is not null && p.CompareTo(document.ContentEnd) < 0;
             p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: Image image })
                image.MaxWidth = max;
        }
    }
}
