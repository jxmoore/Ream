using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Ream.App.Views;

/// <summary>
/// The Home tab of the ribbon: clipboard, font, paragraph and style controls for whichever note editor last
/// had keyboard focus. Buttons never take focus, so the editor's selection and caret stay put while they're used.
/// </summary>
public partial class RibbonView : UserControl
{
    private static readonly string[] PreferredFonts =
        ["Segoe UI", "Arial", "Calibri", "Cambria", "Consolas", "Courier New", "Georgia", "Times New Roman", "Trebuchet MS", "Verdana"];

    private static readonly double[] Sizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 72];

    private static readonly (string Label, double? Size)[] Headings =
        [("Normal", null), ("Heading 1", 28), ("Heading 2", 22), ("Heading 3", 18)];

    private static readonly (string Name, string Hex)[] TextColors =
    [
        ("Red", "#e5484d"), ("Orange", "#f2a65a"), ("Yellow", "#f5d90a"), ("Green", "#46a758"),
        ("Teal", "#12a594"), ("Blue", "#3e63dd"), ("Purple", "#8e4ec6"), ("Pink", "#d6409f"),
        ("Gray", "#8b8d98"), ("White", "#ffffff"), ("Black", "#000000"),
    ];

    private static readonly (string Name, string Hex)[] HighlightColors =
    [
        ("Yellow", "#fff3a3"), ("Green", "#c9f2c7"), ("Blue", "#cfe3ff"),
        ("Pink", "#ffd6e7"), ("Orange", "#ffe0b8"), ("Gray", "#e0e0e0"),
    ];

    private RichTextBox? _editor;
    private bool _refreshing;

    public RibbonView()
    {
        InitializeComponent();

        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FontBox.ItemsSource = PreferredFonts.Where(installed.Contains).ToList();
        SizeBox.ItemsSource = Sizes;

        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            window.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), true);
            window.AddHandler(TextBoxBase.SelectionChangedEvent, new RoutedEventHandler(OnSelectionChanged), true);
        };
    }

    // ----- Tracking the active editor -----

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is RichTextBox editor) AttachEditor(editor);
    }

    internal void AttachEditor(RichTextBox editor)
    {
        if (editor == _editor) return;

        _editor = editor;
        Bar.IsEnabled = true;
        Refresh();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _editor)) Refresh();
    }

    /// <summary>Shows the formatting at the selection (or caret) in the buttons and drop-downs.</summary>
    private void Refresh()
    {
        if (_editor is null || !_editor.IsLoaded) return;

        _refreshing = true;
        try
        {
            var selection = _editor.Selection;

            BoldButton.IsChecked = selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w >= FontWeights.SemiBold;
            ItalicButton.IsChecked = selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;

            var decorations = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            UnderlineButton.IsChecked = HasDecoration(decorations, TextDecorationLocation.Underline);
            StrikeButton.IsChecked = HasDecoration(decorations, TextDecorationLocation.Strikethrough);

            var alignment = selection.GetPropertyValue(Block.TextAlignmentProperty) as TextAlignment?;
            AlignLeftButton.IsChecked = alignment == TextAlignment.Left;
            AlignCenterButton.IsChecked = alignment == TextAlignment.Center;
            AlignRightButton.IsChecked = alignment == TextAlignment.Right;
            AlignJustifyButton.IsChecked = alignment == TextAlignment.Justify;

            var list = selection.Start.Paragraph?.Parent is ListItem { Parent: List parent } ? parent : null;
            bool ordered = list?.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin
                or TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
            BulletsButton.IsChecked = list is not null && !ordered;
            NumbersButton.IsChecked = list is not null && ordered;

            FontBox.SelectedItem = selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family
                ? FontBox.Items.Cast<string>().FirstOrDefault(f => f.Equals(family.Source, StringComparison.OrdinalIgnoreCase))
                : null;

            double? size = selection.GetPropertyValue(TextElement.FontSizeProperty) as double?;
            SizeBox.SelectedItem = size is { } value ? Sizes.Cast<double?>().FirstOrDefault(x => Math.Abs(x!.Value - value) < 0.5) : null;

            int heading = size switch
            {
                >= 26 => 1,
                >= 20 => 2,
                >= 17 when BoldButton.IsChecked == true => 3,
                _ => 0,
            };
            StyleNormalButton.IsChecked = heading == 0;
            StyleHeading1Button.IsChecked = heading == 1;
            StyleHeading2Button.IsChecked = heading == 2;
            StyleHeading3Button.IsChecked = heading == 3;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static bool HasDecoration(TextDecorationCollection? decorations, TextDecorationLocation location) =>
        decorations is not null && decorations.Any(d => d.Location == location);

    // ----- Commands -----

    private void Run(RoutedCommand command)
    {
        if (_editor is null) return;
        command.Execute(null, _editor);
        _editor.Focus();
        Refresh();
    }

    private void OnPaste(object sender, RoutedEventArgs e) => Run(ApplicationCommands.Paste);
    private void OnCut(object sender, RoutedEventArgs e) => Run(ApplicationCommands.Cut);
    private void OnCopy(object sender, RoutedEventArgs e) => Run(ApplicationCommands.Copy);
    private void OnBold(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleBold);
    private void OnItalic(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleItalic);
    private void OnUnderline(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleUnderline);
    private void OnAlignLeft(object sender, RoutedEventArgs e) => Run(EditingCommands.AlignLeft);
    private void OnAlignCenter(object sender, RoutedEventArgs e) => Run(EditingCommands.AlignCenter);
    private void OnAlignRight(object sender, RoutedEventArgs e) => Run(EditingCommands.AlignRight);
    private void OnAlignJustify(object sender, RoutedEventArgs e) => Run(EditingCommands.AlignJustify);
    private void OnBullets(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleBullets);
    private void OnNumbers(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleNumbering);
    private void OnIndent(object sender, RoutedEventArgs e) => Run(EditingCommands.IncreaseIndentation);
    private void OnOutdent(object sender, RoutedEventArgs e) => Run(EditingCommands.DecreaseIndentation);
    private void OnLarger(object sender, RoutedEventArgs e) => Run(EditingCommands.IncreaseFontSize);
    private void OnSmaller(object sender, RoutedEventArgs e) => Run(EditingCommands.DecreaseFontSize);

    private void OnStrikethrough(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;

        var current = _editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool has = HasDecoration(current, TextDecorationLocation.Strikethrough);

        var updated = new TextDecorationCollection((current ?? []).Where(d => d.Location != TextDecorationLocation.Strikethrough));
        if (!has) updated.Add(TextDecorations.Strikethrough[0]);

        Apply(Inline.TextDecorationsProperty, updated);
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || FontBox.SelectedItem is not string name) return;
        Apply(TextElement.FontFamilyProperty, new FontFamily(name));
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || SizeBox.SelectedItem is not double size) return;
        Apply(TextElement.FontSizeProperty, size);
    }

    private void OnStyleTile(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out int index)) ApplyHeading(index);
    }

    /// <summary>Applies a paragraph style (0 = Normal, 1-3 = headings) to the paragraphs in the selection.</summary>
    internal void ApplyHeading(int index)
    {
        if (_editor is null || index < 0 || index >= Headings.Length) return;

        // "Normal" applies the editor's own defaults: TextRange can't be told to clear a property.
        var (_, size) = Headings[index];
        foreach (var paragraph in ParagraphsInSelection(_editor))
        {
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontSizeProperty, size ?? _editor.FontSize);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, size is null ? _editor.FontWeight : FontWeights.Bold);
        }

        _editor.Focus();
        Refresh();
    }

    private static List<Paragraph> ParagraphsInSelection(RichTextBox editor)
    {
        var found = new List<Paragraph>();
        var end = editor.Selection.End;

        for (var p = editor.Selection.Start; p is not null && p.CompareTo(end) <= 0; p = p.GetNextInsertionPosition(LogicalDirection.Forward))
        {
            if (p.Paragraph is { } paragraph && !found.Contains(paragraph)) found.Add(paragraph);
        }

        if (found.Count == 0 && editor.CaretPosition.Paragraph is { } caretParagraph) found.Add(caretParagraph);
        return found;
    }

    private void Apply(DependencyProperty property, object? value)
    {
        if (_editor is null) return;
        _editor.Selection.ApplyPropertyValue(property, value);
        _editor.Focus();
        Refresh();
    }

    // ----- Colors -----

    private void OnTextColor(object sender, RoutedEventArgs e) =>
        ShowColorMenu((Button)sender, TextColors, "Automatic", ApplyTextColor);

    private void OnHighlight(object sender, RoutedEventArgs e) =>
        ShowColorMenu((Button)sender, HighlightColors, "None", ApplyHighlight);

    // Dark enough to read on every highlight color, whichever theme is active.
    private static readonly Color HighlightTextColor = Color.FromRgb(0x1a, 0x1a, 0x22);

    internal void ApplyTextColor(Color? color)
    {
        if (_editor is null) return;

        if (color is { } c) Apply(TextElement.ForegroundProperty, new SolidColorBrush(c));
        else ClearAutomaticColor();
    }

    internal void ApplyHighlight(Color? color)
    {
        if (_editor is null) return;

        if (color is { } c)
        {
            // Text on a pastel highlight must stay dark even in the dark theme.
            _editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(c));
            _editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(HighlightTextColor));
            _editor.Focus();
            Refresh();
        }
        else
        {
            _editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
            ClearAutomaticColor();
        }
    }

    /// <summary>
    /// Returns the selection's text to the "automatic" color. That must be the absence of a color rather
    /// than a copy of today's, or it would stay wrong after a theme change. TextRange can't clear a
    /// property, so mark the range (which also splits runs at its edges) and clear what got marked.
    /// </summary>
    private void ClearAutomaticColor()
    {
        if (_editor is null) return;

        var range = _editor.Selection;
        var marker = new SolidColorBrush(Color.FromArgb(1, 1, 2, 3));
        range.ApplyPropertyValue(TextElement.ForegroundProperty, marker);

        for (var p = range.Start; p is not null && p.CompareTo(range.End) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetAdjacentElement(LogicalDirection.Forward) is TextElement element
                && ReferenceEquals(element.ReadLocalValue(TextElement.ForegroundProperty), marker))
                element.ClearValue(TextElement.ForegroundProperty);
        }

        _editor.Focus();
        Refresh();
    }

    private static void ShowColorMenu(Button anchor, (string Name, string Hex)[] colors, string resetLabel, Action<Color?> apply)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        var reset = new MenuItem { Header = resetLabel };
        reset.Click += (_, _) => apply(null);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());

        foreach (var (name, hex) in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var item = new MenuItem
            {
                Header = name,
                Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                },
            };
            item.Click += (_, _) => apply(color);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
