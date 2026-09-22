using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.Editing;

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

    /// <summary>The styles gallery, in tile order. Size null means "the editor's own default".</summary>
    private static readonly (string Label, double? Size, FontWeight Weight, FontStyle Style)[] Styles =
    [
        ("Normal", null, FontWeights.Normal, FontStyles.Normal),
        ("Heading 1", 28, FontWeights.Bold, FontStyles.Normal),
        ("Heading 2", 22, FontWeights.Bold, FontStyles.Normal),
        ("Heading 3", 18, FontWeights.Bold, FontStyles.Normal),
        ("Heading 4", 15, FontWeights.Bold, FontStyles.Normal),
        ("Title", 34, FontWeights.Bold, FontStyles.Normal),
        ("Subtitle", 18, FontWeights.Normal, FontStyles.Italic),
        ("Quote", 14, FontWeights.Normal, FontStyles.Italic),
    ];

    /// <summary>How far one click of the gallery's Previous/More arrows scrolls (one tile's width).</summary>
    private const double StyleTileWidth = 56;

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

    private static readonly (string Name, string Hex)[] ShadingColors =
    [
        ("Gray-25%", "#d9d9d9"), ("Gray-50%", "#a6a6a6"), ("Blue", "#cfe3ff"),
        ("Green", "#c9f2c7"), ("Yellow", "#fff3a3"), ("Pink", "#ffd6e7"),
    ];

    private RichTextBox? _editor;
    private bool _refreshing;
    private bool _menuOpen;

    public RibbonView()
    {
        InitializeComponent();

        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FontBox.ItemsSource = PreferredFonts.Where(installed.Contains).ToList();
        SizeBox.ItemsSource = Sizes;

        // Watch the property itself: it is what says whether the list is showing, and the opened/closed events
        // do not reliably arrive after it has changed.
        var dropDown = DependencyPropertyDescriptor.FromProperty(ComboBox.IsDropDownOpenProperty, typeof(ComboBox));
        dropDown.AddValueChanged(FontBox, (_, _) => MenuOpenChanged?.Invoke());
        dropDown.AddValueChanged(SizeBox, (_, _) => MenuOpenChanged?.Invoke());

        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            window.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), true);
            window.AddHandler(TextBoxBase.SelectionChangedEvent, new RoutedEventHandler(OnSelectionChanged), true);
        };
    }

    /// <summary>Raised when a drop-down or color menu of this ribbon opens or closes.</summary>
    public event Action? MenuOpenChanged;

    /// <summary>A font/size drop-down or a popup menu is open. The window keeps an auto-hidden ribbon up meanwhile.</summary>
    internal bool IsMenuOpen => FontBox.IsDropDownOpen || SizeBox.IsDropDownOpen || _menuOpen;

    /// <summary>The editor that last had keyboard focus, for anything (Find/Replace) that acts on it from outside the ribbon.</summary>
    internal RichTextBox? CurrentEditor => _editor;

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

            var baseline = selection.GetPropertyValue(Inline.BaselineAlignmentProperty) as BaselineAlignment?;
            SubscriptButton.IsChecked = baseline == BaselineAlignment.Subscript;
            SuperscriptButton.IsChecked = baseline == BaselineAlignment.Superscript;

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

            var currentWeight = selection.GetPropertyValue(TextElement.FontWeightProperty) as FontWeight?;
            var currentStyle = selection.GetPropertyValue(TextElement.FontStyleProperty) as FontStyle?;

            // A tile lights up only when the selection's size, weight and slant all match it exactly -
            // like Word, a run that merely looks similar to a style doesn't count as being in it.
            int active = -1;
            for (int i = 0; i < Styles.Length; i++)
            {
                var (_, styleSize, styleWeight, styleStyle) = Styles[i];
                double expected = styleSize ?? _editor.FontSize;
                if (size is { } currentSize && Math.Abs(currentSize - expected) < 0.5 && currentWeight == styleWeight && currentStyle == styleStyle)
                {
                    active = i;
                    break;
                }
            }

            var tiles = StyleTiles;
            for (int i = 0; i < tiles.Length; i++) tiles[i].IsChecked = i == active;
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
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out int index)) ApplyStyle(index);
    }

    private ToggleButton[] StyleTiles =>
    [
        StyleNormalButton, StyleHeading1Button, StyleHeading2Button, StyleHeading3Button,
        StyleHeading4Button, StyleTitleButton, StyleSubtitleButton, StyleQuoteButton,
    ];

    /// <summary>Applies a gallery style (its index in <see cref="Styles"/>) to the paragraphs in the selection.</summary>
    internal void ApplyStyle(int index)
    {
        if (_editor is null || index < 0 || index >= Styles.Length) return;

        // "Normal" applies the editor's own defaults: TextRange can't be told to clear a property.
        var (_, size, weight, style) = Styles[index];
        foreach (var paragraph in SelectionParagraphs.Of(_editor))
        {
            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontSizeProperty, size ?? _editor.FontSize);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
            range.ApplyPropertyValue(TextElement.FontStyleProperty, style);
        }

        _editor.Focus();
        Refresh();
    }

    // ----- The styles gallery's scroll / expand arrows -----

    private void OnStylesScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        StylesPreviousButton.IsEnabled = StylesScroll.HorizontalOffset > 0.5;
        StylesNextButton.IsEnabled = StylesScroll.HorizontalOffset < StylesScroll.ScrollableWidth - 0.5;
    }

    private void OnStylesPrevious(object sender, RoutedEventArgs e) =>
        StylesScroll.ScrollToHorizontalOffset(StylesScroll.HorizontalOffset - StyleTileWidth);

    private void OnStylesNext(object sender, RoutedEventArgs e) =>
        StylesScroll.ScrollToHorizontalOffset(StylesScroll.HorizontalOffset + StyleTileWidth);

    private void OnStylesAll(object sender, RoutedEventArgs e) =>
        ShowMenu(StylesAllButton, Styles.Select((s, i) => (s.Label, (Action)(() => ApplyStyle(i)))));

    private void Apply(DependencyProperty property, object? value)
    {
        if (_editor is null) return;
        _editor.Selection.ApplyPropertyValue(property, value);
        _editor.Focus();
        Refresh();
    }

    // ----- Font: subscript/superscript, clear formatting, change case -----

    private void OnSubscript(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Subscript);
    private void OnSuperscript(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Superscript);

    private void ToggleBaseline(BaselineAlignment target)
    {
        if (_editor is null) return;
        bool isOn = _editor.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty) is BaselineAlignment a && a == target;
        Apply(Inline.BaselineAlignmentProperty, isOn ? BaselineAlignment.Baseline : target);
    }

    private void OnClearFormatting(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _editor.Selection.ClearAllProperties();
        _editor.Focus();
        Refresh();
    }

    private void OnChangeCase(object sender, RoutedEventArgs e) => ShowMenu((Button)sender,
    [
        ("Sentence case.", () => TransformCase(ToSentenceCase)),
        ("lowercase", () => TransformCase(t => t.ToLowerInvariant())),
        ("UPPERCASE", () => TransformCase(t => t.ToUpperInvariant())),
        ("Capitalize Each Word", () => TransformCase(t => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(t.ToLowerInvariant()))),
        ("tOGGLE cASE", () => TransformCase(ToggleCase)),
    ]);

    internal void TransformCase(Func<string, string> transform)
    {
        if (_editor is null || _editor.Selection.IsEmpty) return;
        _editor.Selection.Text = transform(_editor.Selection.Text);
        _editor.Focus();
        Refresh();
    }

    private static string ToSentenceCase(string text)
    {
        var chars = text.ToLowerInvariant().ToCharArray();
        bool startOfSentence = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (startOfSentence && char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                startOfSentence = false;
            }
            else if (chars[i] is '.' or '!' or '?')
            {
                startOfSentence = true;
            }
        }
        return new string(chars);
    }

    private static string ToggleCase(string text) =>
        new(text.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());

    // ----- Paragraph: line spacing, shading, borders, sort -----

    private void OnLineSpacing(object sender, RoutedEventArgs e) => ShowMenu((Button)sender,
    [
        ("1.0", () => ApplyLineSpacing(1.0)),
        ("1.15", () => ApplyLineSpacing(1.15)),
        ("1.5", () => ApplyLineSpacing(1.5)),
        ("2.0", () => ApplyLineSpacing(2.0)),
        ("2.5", () => ApplyLineSpacing(2.5)),
        ("3.0", () => ApplyLineSpacing(3.0)),
        ("Add Space Before Paragraph", () => SetParagraphSpacing(before: 8)),
        ("Remove Space Before Paragraph", () => SetParagraphSpacing(before: 0)),
        ("Add Space After Paragraph", () => SetParagraphSpacing(after: 8)),
        ("Remove Space After Paragraph", () => SetParagraphSpacing(after: 0)),
    ]);

    /// <summary>Sets a paragraph's line height as a multiple of its single-line height (there is no per-run line metric to read back, so this only ever sets).</summary>
    internal void ApplyLineSpacing(double multiplier)
    {
        if (_editor is null) return;
        double lineHeight = Math.Round(_editor.FontSize * 1.2 * multiplier, 1);
        foreach (var paragraph in SelectionParagraphs.Of(_editor)) paragraph.LineHeight = lineHeight;
        _editor.Focus();
        Refresh();
    }

    private void SetParagraphSpacing(double? before = null, double? after = null)
    {
        if (_editor is null) return;
        foreach (var paragraph in SelectionParagraphs.Of(_editor))
        {
            var margin = paragraph.Margin;
            paragraph.Margin = new Thickness(margin.Left, before ?? margin.Top, margin.Right, after ?? margin.Bottom);
        }
        _editor.Focus();
        Refresh();
    }

    private void OnShading(object sender, RoutedEventArgs e) =>
        ShowColorMenu((Button)sender, ShadingColors, "No Color", ApplyShading);

    internal void ApplyShading(Color? color)
    {
        if (_editor is null) return;
        foreach (var paragraph in SelectionParagraphs.Of(_editor))
            paragraph.Background = color is { } c ? new SolidColorBrush(c) : null;
        _editor.Focus();
        Refresh();
    }

    private void OnBorders(object sender, RoutedEventArgs e) => ShowMenu((Button)sender,
    [
        ("No Border", () => ApplyBorder((_, _) => new Thickness(0))),
        ("All Borders", () => ApplyBorder((_, _) => new Thickness(1))),
        ("Outside Borders", () => ApplyBorder((i, n) => new Thickness(1, i == 0 ? 1 : 0, 1, i == n - 1 ? 1 : 0))),
        ("Bottom Border", () => ApplyBorder((_, _) => new Thickness(0, 0, 0, 1))),
        ("Top Border", () => ApplyBorder((_, _) => new Thickness(0, 1, 0, 0))),
    ]);

    // A fixed color, not a theme resource: NoteDocumentSerializer re-creates a loaded border with this
    // same color (the persistence layer doesn't know about themes), so a border looks the same before
    // and after a save/reload either way.
    private static readonly Brush BorderColor = CreateBorderBrush();
    private static SolidColorBrush CreateBorderBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        brush.Freeze();
        return brush;
    }

    /// <summary><paramref name="thicknessFor"/> is given each paragraph's position (index, count) in the selection, for "outside" borders.</summary>
    internal void ApplyBorder(Func<int, int, Thickness> thicknessFor)
    {
        if (_editor is null) return;

        var paragraphs = SelectionParagraphs.Of(_editor);
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var thickness = thicknessFor(i, paragraphs.Count);
            bool none = thickness.Left == 0 && thickness.Top == 0 && thickness.Right == 0 && thickness.Bottom == 0;
            paragraphs[i].BorderThickness = thickness;
            paragraphs[i].BorderBrush = none ? null : BorderColor;
            paragraphs[i].Padding = none ? new Thickness(0) : new Thickness(4, 2, 4, 2);
        }

        _editor.Focus();
        Refresh();
    }

    private void OnSort(object sender, RoutedEventArgs e) => ShowMenu((Button)sender,
    [
        ("Sort Ascending", () => SortParagraphs(ascending: true)),
        ("Sort Descending", () => SortParagraphs(ascending: false)),
    ]);

    /// <summary>Reorders the text of the selected paragraphs alphabetically; each paragraph keeps its own formatting, only its text moves.</summary>
    internal void SortParagraphs(bool ascending)
    {
        if (_editor is null) return;

        var paragraphs = SelectionParagraphs.Of(_editor);
        if (paragraphs.Count < 2) return;

        var texts = paragraphs.Select(p => new TextRange(p.ContentStart, p.ContentEnd).Text).ToList();
        var ordered = ascending
            ? texts.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            : texts.OrderByDescending(t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var (paragraph, text) in paragraphs.Zip(ordered))
            new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text = text;

        _editor.Focus();
        Refresh();
    }

    // ----- Editing: select -----

    private void OnSelect(object sender, RoutedEventArgs e) => ShowMenu((Button)sender,
    [
        ("Select All", () => { _editor?.SelectAll(); _editor?.Focus(); }),
        ("Select Paragraph", SelectCurrentParagraph),
    ]);

    private void SelectCurrentParagraph()
    {
        if (_editor?.CaretPosition.Paragraph is not { } paragraph) return;
        _editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
        _editor.Focus();
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

    private void ShowColorMenu(Button anchor, (string Name, string Hex)[] colors, string resetLabel, Action<Color?> apply)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        menu.Opened += (_, _) => SetMenuOpen(true);
        menu.Closed += (_, _) => SetMenuOpen(false);

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

    // ----- A plain text drop-down menu, for the buttons that offer a short list of actions rather than colors -----

    private void ShowMenu(Button anchor, IEnumerable<(string Label, Action Action)> items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        menu.Opened += (_, _) => SetMenuOpen(true);
        menu.Closed += (_, _) => SetMenuOpen(false);

        foreach (var (label, action) in items)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        MenuOpenChanged?.Invoke();
    }
}
