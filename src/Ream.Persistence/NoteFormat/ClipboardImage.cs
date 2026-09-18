using System.Windows;
using System.Windows.Media.Imaging;

namespace Ream.Persistence.NoteFormat;

/// <summary>Decides when a paste is "an image" and extracts it as PNG bytes.</summary>
public static class ClipboardImage
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];
    private static readonly string[] TextFormats = [DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Xaml];

    /// <summary>
    /// True for a picture with no accompanying text (a screenshot, "copy image"). Mixed content such as
    /// text copied from a web page is left to the editor's normal paste so its text isn't lost.
    /// </summary>
    public static bool HasPastableImage(IDataObject data) =>
        (data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap))
        && !TextFormats.Any(format => data.GetDataPresent(format));

    public static bool TryGetPng(IDataObject data, out byte[] png)
    {
        png = [];

        // Prefer the "PNG" format: it keeps transparency, which the plain bitmap format loses.
        if (data.GetDataPresent("PNG") && ReadBytes(data.GetData("PNG")) is { Length: > 0 } raw)
        {
            if (IsPng(raw))
            {
                png = raw;
                return true;
            }

            if (NoteImage.FromPng(raw) is { } decoded)
            {
                png = NoteImage.EncodePng(decoded);
                return true;
            }
        }

        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            png = NoteImage.EncodePng(bitmap);
            return true;
        }

        return false;
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length > PngSignature.Length && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);

    private static byte[]? ReadBytes(object? data)
    {
        switch (data)
        {
            case byte[] bytes:
                return bytes;
            case Stream stream:
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            default:
                return null;
        }
    }
}
