using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ream.Persistence.NoteFormat;

/// <summary>Image elements inside a note: which asset file backs them, and how to create/load/encode them.</summary>
public static class NoteImage
{
    private const double MaxInitialWidth = 800;

    public static readonly DependencyProperty AssetNameProperty = DependencyProperty.RegisterAttached(
        "AssetName", typeof(string), typeof(NoteImage), new PropertyMetadata(null));

    public static string? GetAssetName(DependencyObject element) => (string?)element.GetValue(AssetNameProperty);

    public static void SetAssetName(DependencyObject element, string? name) => element.SetValue(AssetNameProperty, name);

    /// <summary>Creates the image element shown in the editor. Size defaults to the picture's natural size, capped.</summary>
    public static Image Create(ImageSource? source, string assetName, double? width = null, double? height = null)
    {
        // PNG stores resolution as whole pixels-per-metre, so natural sizes carry tiny errors; round them.
        double w = width ?? Math.Round(source?.Width ?? 100);
        double h = height ?? Math.Round(source?.Height ?? 100);

        if (width is null && w > MaxInitialWidth)
        {
            h = Math.Round(h * MaxInitialWidth / w);
            w = MaxInitialWidth;
        }

        var image = new Image
        {
            Source = source,
            Width = Math.Max(1, w),
            Height = Math.Max(1, h),
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
        };
        SetAssetName(image, assetName);
        return image;
    }

    /// <summary>Decodes PNG bytes fully into memory, so no file handle stays open.</summary>
    public static BitmapSource? FromPng(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Loads an image file fully into memory (so the file can later be moved or replaced), or null if unreadable.</summary>
    public static BitmapSource? LoadFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
