using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Ream.Core.Models;

/// <summary>
/// WPF-free helpers for the .reamnote format: recognising it, parsing it safely,
/// and extracting plain text (for titles) without needing a UI thread.
/// </summary>
public static class NoteContent
{
    public const string RootName = "ReamNote";
    public const string BodyName = "Doc";
    public const int SchemaVersion = 1;
    private const int MaxTitleLength = 60;

    public static bool IsReamNote(string content)
    {
        var trimmed = content.AsSpan().TrimStart();
        return trimmed.StartsWith("<?xml", StringComparison.Ordinal)
            || trimmed.StartsWith("<" + RootName, StringComparison.Ordinal);
    }

    /// <summary>Parses note XML. DTDs are refused so a note file can't trigger entity-expansion attacks.</summary>
    public static bool TryParse(string content, out XDocument document)
    {
        document = null!;
        if (!IsReamNote(content)) return false;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var reader = XmlReader.Create(new StringReader(content), settings);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            return document.Root?.Name.LocalName == RootName;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Plain text of a note: paragraphs separated by newlines. Non-note content is returned as is.</summary>
    public static string ToPlainText(string content)
    {
        if (!IsReamNote(content)) return content;
        if (!TryParse(content, out var document)) return content;

        var body = document.Root!.Element(BodyName);
        if (body is null) return "";

        var text = new StringBuilder();
        AppendBlocks(body, text);
        return text.ToString().TrimEnd('\n');
    }

    /// <summary>First non-empty line, trimmed and capped; null if the text is blank.</summary>
    public static string? TryDeriveTitle(string plainText)
    {
        foreach (var line in plainText.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
        }
        return null;
    }

    public static string DeriveTitle(string plainText) => TryDeriveTitle(plainText) ?? "Untitled";

    private static void AppendBlocks(XElement container, StringBuilder text)
    {
        foreach (var element in container.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "P":
                    foreach (var inline in element.Elements())
                    {
                        if (inline.Name.LocalName == "R") text.Append(inline.Value);
                        else if (inline.Name.LocalName == "BR") text.Append('\n');
                    }
                    text.Append('\n');
                    break;
                case "UL":
                case "OL":
                    foreach (var item in element.Elements("LI"))
                        AppendBlocks(item, text);
                    break;
            }
        }
    }
}
