using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Ream.Core.Models;

namespace Ream.Persistence.NoteFormat;

/// <summary>
/// Converts between a FlowDocument and the .reamnote XML format.
/// Only a fixed set of elements is read (paragraphs, runs, line breaks, images, lists), so a note
/// file can never make the app construct arbitrary objects the way loading raw XAML would.
/// Must be used on the UI thread that owns the document.
/// </summary>
public static class NoteDocumentSerializer
{
    private const string AssetPrefix = "asset://";
    private const double SizeTolerance = 0.01;

    // ---------- Writing ----------

    /// <param name="saveAsset">Stores PNG bytes and returns an asset name; used only for images not yet backed by a file.</param>
    public static string Serialize(FlowDocument document, Guid noteId, Func<byte[], string> saveAsset)
    {
        var body = new XElement(NoteContent.BodyName);
        WriteBlocks(document.Blocks, body, document, saveAsset);

        var root = new XElement(
            NoteContent.RootName,
            new XAttribute("schemaVersion", NoteContent.SchemaVersion),
            new XAttribute("id", noteId.ToString("D")),
            body);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.Entitize,
        };
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, settings))
            root.Save(writer);
        return text.ToString();
    }

    private static void WriteBlocks(IEnumerable<Block> blocks, XElement parent, FlowDocument document, Func<byte[], string> saveAsset)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    parent.Add(WriteParagraph(paragraph, document, saveAsset));
                    break;
                case List list:
                    parent.Add(WriteList(list, document, saveAsset));
                    break;
                case Section section:
                    WriteBlocks(section.Blocks, parent, document, saveAsset);
                    break;
                case BlockUIContainer { Child: Image image }:
                    var wrapper = new XElement("P");
                    if (WriteImage(image, saveAsset) is { } imageElement) wrapper.Add(imageElement);
                    parent.Add(wrapper);
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                WriteBlocks(cell.Blocks, parent, document, saveAsset);
                    break;
            }
        }
    }

    private static XElement WriteParagraph(Paragraph paragraph, FlowDocument document, Func<byte[], string> saveAsset)
    {
        var element = new XElement("P");

        if (paragraph.TextAlignment != document.TextAlignment)
            element.SetAttributeValue("align", paragraph.TextAlignment.ToString().ToLowerInvariant());
        WriteFormatting(element, TextStyle.Of(paragraph), TextStyle.Of(document));

        WriteInlines(paragraph.Inlines, paragraph, element, saveAsset);
        return element;
    }

    private static void WriteInlines(IEnumerable<Inline> inlines, Paragraph paragraph, XElement parent, Func<byte[], string> saveAsset)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length == 0) break;
                    var element = new XElement("R");
                    WriteFormatting(element, TextStyle.Of(run), TextStyle.Of(paragraph));
                    element.Value = Sanitize(run.Text);
                    parent.Add(element);
                    break;
                case LineBreak:
                    parent.Add(new XElement("BR"));
                    break;
                case InlineUIContainer { Child: Image image }:
                    if (WriteImage(image, saveAsset) is { } imageElement) parent.Add(imageElement);
                    break;
                case Span span:
                    // Bold, Italic, Underline, Hyperlink: their effect is already in each run's effective values.
                    WriteInlines(span.Inlines, paragraph, parent, saveAsset);
                    break;
            }
        }
    }

    /// <summary>Writes only what differs from the baseline, so inherited defaults aren't baked into every run.</summary>
    private static void WriteFormatting(XElement element, TextStyle actual, TextStyle baseline)
    {
        if (!string.Equals(actual.Family.Source, baseline.Family.Source, StringComparison.OrdinalIgnoreCase))
            element.SetAttributeValue("font", actual.Family.Source);
        if (Math.Abs(actual.Size - baseline.Size) > SizeTolerance)
            element.SetAttributeValue("size", Number(actual.Size));
        if (actual.Weight != baseline.Weight)
            element.SetAttributeValue("b", actual.Weight.ToOpenTypeWeight() >= 600 ? "1" : "0");
        if (actual.Style != baseline.Style)
            element.SetAttributeValue("i", actual.Style == FontStyles.Normal ? "0" : "1");

        bool underline = Has(actual.Decorations, TextDecorationLocation.Underline);
        bool strike = Has(actual.Decorations, TextDecorationLocation.Strikethrough);
        if (underline != Has(baseline.Decorations, TextDecorationLocation.Underline)
            || strike != Has(baseline.Decorations, TextDecorationLocation.Strikethrough))
        {
            element.SetAttributeValue("u", underline ? "1" : "0");
            element.SetAttributeValue("s", strike ? "1" : "0");
        }

        string? color = ColorText(actual.Foreground);
        if (color is not null && color != ColorText(baseline.Foreground))
            element.SetAttributeValue("color", color);
        string? bg = ColorText(actual.Background);
        if (bg != ColorText(baseline.Background))
            element.SetAttributeValue("bg", bg ?? "none");
    }

    /// <summary>The effective look of a document, paragraph, or run, for comparing against what it inherits.</summary>
    private sealed record TextStyle(
        FontFamily Family,
        double Size,
        FontWeight Weight,
        FontStyle Style,
        TextDecorationCollection? Decorations,
        Brush? Foreground,
        Brush? Background)
    {
        public static TextStyle Of(FlowDocument d) =>
            new(d.FontFamily, d.FontSize, d.FontWeight, d.FontStyle, null, d.Foreground, d.Background);

        public static TextStyle Of(Paragraph p) =>
            new(p.FontFamily, p.FontSize, p.FontWeight, p.FontStyle, p.TextDecorations, p.Foreground, p.Background);

        public static TextStyle Of(Run r) =>
            new(r.FontFamily, r.FontSize, r.FontWeight, r.FontStyle, r.TextDecorations, r.Foreground, r.Background);
    }

    private static XElement WriteList(List list, FlowDocument document, Func<byte[], string> saveAsset)
    {
        bool ordered = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin
            or TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
        var element = new XElement(ordered ? "OL" : "UL");

        var defaultStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
        if (list.MarkerStyle != defaultStyle)
            element.SetAttributeValue("type", list.MarkerStyle.ToString());

        foreach (var item in list.ListItems)
        {
            var itemElement = new XElement("LI");
            WriteBlocks(item.Blocks, itemElement, document, saveAsset);
            element.Add(itemElement);
        }
        return element;
    }

    private static XElement? WriteImage(Image image, Func<byte[], string> saveAsset)
    {
        string? name = NoteImage.GetAssetName(image);
        if (name is null)
        {
            // An image that arrived some way other than our paste handler (e.g. rich-text paste).
            if (image.Source is not System.Windows.Media.Imaging.BitmapSource bitmap) return null;
            name = saveAsset(NoteImage.EncodePng(bitmap));
            NoteImage.SetAssetName(image, name);
        }

        var element = new XElement("IMG", new XAttribute("src", AssetPrefix + name));
        if (double.IsFinite(image.Width) && image.Width > 0) element.SetAttributeValue("w", Number(image.Width));
        if (double.IsFinite(image.Height) && image.Height > 0) element.SetAttributeValue("h", Number(image.Height));
        return element;
    }

    // ---------- Reading ----------

    /// <param name="loadImage">Resolves an asset name to a picture, or null if the file is missing.</param>
    public static FlowDocument Deserialize(string content, Func<string, ImageSource?> loadImage)
    {
        if (!NoteContent.TryParse(content, out var xml))
            return FromPlainText(content);

        var document = new FlowDocument();
        if (xml.Root!.Element(NoteContent.BodyName) is { } body)
            ReadBlocks(body, document.Blocks, loadImage);

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());
        return document;
    }

    /// <summary>For text that isn't in note format (older notes, files dropped in by hand).</summary>
    public static FlowDocument FromPlainText(string text)
    {
        var document = new FlowDocument();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var paragraph = new Paragraph();
            if (line.Length > 0) paragraph.Inlines.Add(new Run(line));
            document.Blocks.Add(paragraph);
        }
        return document;
    }

    private static void ReadBlocks(XElement container, BlockCollection blocks, Func<string, ImageSource?> loadImage)
    {
        foreach (var element in container.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "P":
                    blocks.Add(ReadParagraph(element, loadImage));
                    break;
                case "UL":
                case "OL":
                    blocks.Add(ReadList(element, ordered: element.Name.LocalName == "OL", loadImage));
                    break;
            }
        }
    }

    private static Paragraph ReadParagraph(XElement element, Func<string, ImageSource?> loadImage)
    {
        var paragraph = new Paragraph();
        if (Enum.TryParse<TextAlignment>((string?)element.Attribute("align"), ignoreCase: true, out var align)
            && Enum.IsDefined(align))
            paragraph.TextAlignment = align;
        ApplyFormatting(paragraph, element);

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "R":
                    var run = new Run(child.Value);
                    ApplyFormatting(run, child);
                    paragraph.Inlines.Add(run);
                    break;
                case "BR":
                    paragraph.Inlines.Add(new LineBreak());
                    break;
                case "IMG":
                    if (ReadImage(child, loadImage) is { } container) paragraph.Inlines.Add(container);
                    break;
            }
        }
        return paragraph;
    }

    private static List ReadList(XElement element, bool ordered, Func<string, ImageSource?> loadImage)
    {
        var list = new List { MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };

        if (Enum.TryParse<TextMarkerStyle>((string?)element.Attribute("type"), ignoreCase: true, out var style)
            && Enum.IsDefined(style))
            list.MarkerStyle = style;

        foreach (var itemElement in element.Elements("LI"))
        {
            var item = new ListItem();
            ReadBlocks(itemElement, item.Blocks, loadImage);
            if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
            list.ListItems.Add(item);
        }
        return list;
    }

    private static InlineUIContainer? ReadImage(XElement element, Func<string, ImageSource?> loadImage)
    {
        string source = (string?)element.Attribute("src") ?? "";
        if (!source.StartsWith(AssetPrefix, StringComparison.Ordinal)) return null;

        string name = source[AssetPrefix.Length..];
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            return null;

        var picture = loadImage(name);
        var image = NoteImage.Create(
            picture, name,
            ParseDouble((string?)element.Attribute("w"), 1, 20000),
            ParseDouble((string?)element.Attribute("h"), 1, 20000));
        if (picture is null) image.ToolTip = $"Missing image: {name}";

        return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>Applies whatever formatting attributes are present; malformed values are ignored.</summary>
    private static void ApplyFormatting(TextElement target, XElement element)
    {
        if ((string?)element.Attribute("font") is { Length: > 0 } font)
        {
            try { target.FontFamily = new FontFamily(font); }
            catch (ArgumentException) { }
        }

        if (ParseDouble((string?)element.Attribute("size"), 1, 500) is { } size)
            target.FontSize = size;
        if ((string?)element.Attribute("b") is { } bold)
            target.FontWeight = bold == "1" ? FontWeights.Bold : FontWeights.Normal;
        if ((string?)element.Attribute("i") is { } italic)
            target.FontStyle = italic == "1" ? FontStyles.Italic : FontStyles.Normal;

        string? underline = (string?)element.Attribute("u");
        string? strike = (string?)element.Attribute("s");
        if (underline is not null || strike is not null)
        {
            var decorations = new TextDecorationCollection();
            if (underline == "1") decorations.Add(TextDecorations.Underline[0]);
            if (strike == "1") decorations.Add(TextDecorations.Strikethrough[0]);
            decorations.Freeze();
            SetDecorations(target, decorations);
        }

        if (ParseBrush((string?)element.Attribute("color")) is { } foreground)
            target.Foreground = foreground;

        string? bg = (string?)element.Attribute("bg");
        if (bg == "none") target.Background = null;
        else if (ParseBrush(bg) is { } background) target.Background = background;
    }

    // ---------- Helpers ----------

    private static void SetDecorations(TextElement target, TextDecorationCollection decorations)
    {
        switch (target)
        {
            case Inline inline: inline.TextDecorations = decorations; break;
            case Paragraph paragraph: paragraph.TextDecorations = decorations; break;
        }
    }

    private static bool Has(TextDecorationCollection? decorations, TextDecorationLocation location) =>
        decorations is not null && decorations.Any(d => d.Location == location);

    private static string? ColorText(Brush? brush)
    {
        if (brush is not SolidColorBrush { Color: var c }) return null;
        return c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static Brush? ParseBrush(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '#') return null;
        try
        {
            if (ColorConverter.ConvertFromString(text) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException) { }
        catch (NotSupportedException) { }
        return null;
    }

    private static double? ParseDouble(string? text, double min, double max) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= min && value <= max
            ? value
            : null;

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>XML can't carry most control characters; drop them rather than fail to save.</summary>
    private static string Sanitize(string text)
    {
        var clean = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                clean.Append(c).Append(text[++i]);
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                clean.Append(c);
            }
        }
        return clean.ToString();
    }
}
