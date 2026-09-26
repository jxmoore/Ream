using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ream.App.Controls;
using Ream.App.Editing;
using Ream.App.ViewModels;
using Ream.Core.Layout;
using Ream.Core.Models;
using Ream.Persistence.NoteFormat;

namespace Ream.App.Views;

public partial class NoteColumnView : UserControl, INearAware
{
    private NoteViewModel? _note;
    private bool _subscribed;
    private bool _loadingDocument;
    private bool _hasContent;
    private bool _loadQueued;

    // Outline view: the real document, stashed away while Editor.Document shows a generated summary instead.
    private FlowDocument? _liveDocument;
    private Dictionary<Paragraph, Paragraph>? _outlineMap;

    public NoteColumnView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();

        Editor.TextChanged += OnTextChanged;
        Editor.SizeChanged += (_, _) => FitImages();
        Editor.GotKeyboardFocus += (_, _) => _note?.FocusCommand.Execute(null);
        Editor.PreviewMouseLeftButtonDown += OnEditorPreviewMouseDown;
        DataObject.AddPastingHandler(Editor, OnPaste);
    }

    /// <summary>True once this view has parsed its note into the editor (deferred until the note is near the viewport).</summary>
    internal bool IsContentLoaded => _hasContent;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe();
        _note = e.NewValue as NoteViewModel;
        _hasContent = false;
        if (_note is null) return;

        // Shows nothing until the note is loaded; also drops any previous note's text.
        _loadingDocument = true;
        try { Editor.Document = new FlowDocument(); }
        finally { _loadingDocument = false; }

        _liveDocument = null;
        _outlineMap = null;

        if (!IsLoaded) return;
        Subscribe();
        if (NoteRowPanel.GetIsNear(this)) EnsureLoaded();
    }

    private void OnLoaded()
    {
        Subscribe();
        if (NoteRowPanel.GetIsNear(this)) EnsureLoaded();
    }

    // Called by the row as this column moves in or out of the zone worth loading. Loading waits for a
    // background-priority turn so it never happens in the middle of a layout pass.
    void INearAware.OnNearChanged(bool isNear)
    {
        if (!isNear || _hasContent || _loadQueued) return;

        _loadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _loadQueued = false;
            EnsureLoaded();
        });
    }

    /// <summary>Parses the note into the editor. Does nothing if that has already happened.</summary>
    internal void EnsureLoaded()
    {
        if (_hasContent || _note is null) return;
        _hasContent = true;

        _loadingDocument = true;
        try
        {
            // A FlowDocument doesn't inherit look-and-feel from its RichTextBox, so give it the editor's
            // defaults. They also become the baseline the saved file is diffed against.
            var document = _note.OpenDocument();
            document.FontFamily = Editor.FontFamily;
            document.FontSize = Editor.FontSize;
            // A resource reference, not a copy, so the default text color follows theme changes.
            document.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
            Editor.Document = document;
        }
        finally
        {
            _loadingDocument = false;
        }

        FitImages();
        ApplyImageVisibility();
        if (_note.IsOutlineView) ApplyOutlineView();
    }

    private void Subscribe()
    {
        if (_note is null || _subscribed) return;
        _note.EditorFocusRequested += OnEditorFocusRequested;
        _note.SizeToastRequested += OnSizeToast;
        _note.PropertyChanged += OnNotePropertyChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_note is null || !_subscribed) return;
        _note.EditorFocusRequested -= OnEditorFocusRequested;
        _note.SizeToastRequested -= OnSizeToast;
        _note.PropertyChanged -= OnNotePropertyChanged;
        _subscribed = false;
    }

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NoteViewModel.HideImages):
                ApplyImageVisibility();
                break;
            case nameof(NoteViewModel.IsOutlineView):
                ApplyOutlineView();
                break;
        }
    }

    private static readonly TimeSpan ToastHold = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ToastFade = TimeSpan.FromMilliseconds(300);

    /// <summary>Shows the width for a moment in the accent color, then fades it (or just hides it if animations are off).</summary>
    private void OnSizeToast(string text)
    {
        SizeToast.Text = text;

        bool animate = FindRow()?.Config.Animations.Enabled ?? true;
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(ToastHold)));
        if (animate) frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(ToastHold + ToastFade)));
        else frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(ToastHold + TimeSpan.FromMilliseconds(1))));
        frames.FillBehavior = FillBehavior.Stop;

        SizeToast.Opacity = 0;
        SizeToast.BeginAnimation(OpacityProperty, frames);
    }

    private void OnUnloaded()
    {
        Unsubscribe();
        _note?.FlushDocument();
    }

    private void OnEditorFocusRequested()
    {
        EnsureLoaded();

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

    // ----- Renaming -----

    private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        _note?.BeginTitleEdit();
        e.Handled = true;
    }

    private void OnTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (_note is null) return;

        switch (e.Key)
        {
            case Key.Enter:
                _note.CommitTitleEdit();
                _note.RequestEditorFocus();
                e.Handled = true;
                break;
            case Key.Escape:
                _note.CancelTitleEdit();
                _note.RequestEditorFocus();
                e.Handled = true;
                break;
        }
    }

    // Clicking away keeps what was typed (and leaves focus where the click put it). Escape has already ended the edit.
    private void OnTitleLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _note?.CommitTitleEdit();

    private void OnTitleBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;

        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    // ----- Resizing -----

    // The width being dragged to, tracked here (not read back from layout) so several drag events
    // arriving before a layout pass can't lose any movement.
    private double _dragWidth;

    private NoteRowPanel? FindRow()
    {
        for (DependencyObject? d = this; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is NoteRowPanel row) return row;
        return null;
    }

    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        if (_note is null) return;
        _dragWidth = ActualWidth;
        _note.IsResizing = true;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (_note is null || _note.IsFullscreen || FindRow() is not { } row) return;

        double viewport = row.ActualWidth;
        double gap = row.Config.Layout.GapPx;

        double min = RowLayout.ColumnWidth(WidthPresets.Min, viewport, gap);
        double max = RowLayout.ColumnWidth(WidthPresets.Max, viewport, gap);
        _dragWidth = Math.Clamp(_dragWidth + e.HorizontalChange, min, max);

        _note.WidthFraction = RowLayout.FractionForWidth(_dragWidth, viewport, gap);
        _note.ShowSizeToast();
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_note is not null) _note.IsResizing = false;
    }

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
        EnsureLoaded();

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
        ApplyImageVisibility();
    }

    /// <summary>Draft view: shows or hides every picture in the real document (nothing is removed - still saved, still there when Draft is off). A no-op while Outline view is showing its generated summary instead.</summary>
    private void ApplyImageVisibility()
    {
        if (_note is null || _liveDocument is not null) return;
        if (Editor.Document is not { } document) return;

        var visibility = _note.HideImages ? Visibility.Collapsed : Visibility.Visible;
        for (var p = document.ContentStart;
             p is not null && p.CompareTo(document.ContentEnd) < 0;
             p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: Image image })
                image.Visibility = visibility;
        }
    }

    // ----- Outline view -----

    /// <summary>Swaps <see cref="Editor"/>'s document to (or back from) a generated, read-only summary of just the heading paragraphs.</summary>
    private void ApplyOutlineView()
    {
        if (_note is null || !_hasContent) return;

        if (_note.IsOutlineView)
        {
            if (_liveDocument is not null || Editor.Document is not { } real) return;

            _liveDocument = real;
            _loadingDocument = true;
            try { Editor.Document = BuildOutlineDocument(real); }
            finally { _loadingDocument = false; }
        }
        else
        {
            if (_liveDocument is not { } real) return;

            _liveDocument = null;
            _outlineMap = null;
            _loadingDocument = true;
            try { Editor.Document = real; }
            finally { _loadingDocument = false; }

            ApplyImageVisibility();
        }
    }

    /// <summary>One line per heading paragraph (<see cref="NoteStyles.HeadingLevelOf"/>), styled and indented to match, click-mapped back to the real paragraph via <see cref="_outlineMap"/>.</summary>
    private FlowDocument BuildOutlineDocument(FlowDocument real)
    {
        var outline = new FlowDocument { FontFamily = real.FontFamily, FontSize = real.FontSize };
        outline.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");

        _outlineMap = [];

        foreach (var block in real.Blocks)
        {
            if (block is not Paragraph realParagraph) continue;
            if (NoteStyles.HeadingLevelOf(realParagraph, real) is not { } level) continue;

            string text = new TextRange(realParagraph.ContentStart, realParagraph.ContentEnd).Text.Trim();
            if (text.Length == 0) continue;

            var (_, size, weight, style) = NoteStyles.All[level];
            var line = new Paragraph(new Run(text))
            {
                FontSize = size ?? real.FontSize,
                FontWeight = weight,
                FontStyle = style,
                Margin = new Thickness((level - 1) * 20, 0, 0, 6),
                Cursor = Cursors.Hand,
            };
            outline.Blocks.Add(line);
            _outlineMap[line] = realParagraph;
        }

        if (outline.Blocks.Count == 0)
        {
            var empty = new Paragraph(new Run("No headings in this note.")) { FontStyle = FontStyles.Italic };
            empty.SetResourceReference(TextElement.ForegroundProperty, "MutedTextBrush");
            outline.Blocks.Add(empty);
        }

        return outline;
    }

    /// <summary>Clicking a line in the outline turns Outline view off and puts the caret at the real paragraph it summarizes.</summary>
    private void OnEditorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_note is null || _outlineMap is null) return;

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
        if (position is null) return;

        DependencyObject? scope = position.Parent;
        while (scope is TextElement element and not Paragraph)
            scope = element.Parent;

        if (scope is not Paragraph clicked || !_outlineMap.TryGetValue(clicked, out var real)) return;

        e.Handled = true;
        _note.IsOutlineView = false; // synchronously restores Editor.Document via OnNotePropertyChanged -> ApplyOutlineView

        Editor.CaretPosition = real.ContentStart;
        Editor.Focus();
        real.BringIntoView();
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
