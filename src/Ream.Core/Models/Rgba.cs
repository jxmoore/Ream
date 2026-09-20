using System.Globalization;

namespace Ream.Core.Models;

/// <summary>A color as it is written in config.json: "#RRGGBB" or "#AARRGGBB".</summary>
public readonly record struct Rgba(byte A, byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out Rgba color)
    {
        color = default;
        if (text is null) return false;

        text = text.Trim();
        if (text.Length is not (7 or 9) || text[0] != '#') return false;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
            return false;

        color = text.Length == 7
            ? new Rgba(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new Rgba((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}
