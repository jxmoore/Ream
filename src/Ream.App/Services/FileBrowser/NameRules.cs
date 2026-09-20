namespace Ream.App.Services.FileBrowser;

/// <summary>Why a typed file or folder name is not acceptable, in words for the person typing it.</summary>
internal static class NameRules
{
    public const int MaxReamNameLength = 100;
    public const int MaxFolderNameLength = 255;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Null when the name is fine, else a short message. <paramref name="noun"/> is "ream" or "folder".</summary>
    public static string? Problem(string name, string noun, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(name)) return $"Type a name for the {noun}.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A name can't contain any of these characters: \\ / : * ? \" < > |";
        if (name != name.Trim()) return "A name can't start or end with a space.";
        if (name.EndsWith('.')) return "A name can't end with a period.";
        if (name.Length > maxLength) return $"Use a shorter name ({maxLength} characters at most).";

        // Windows reserves a device name with any extension too ("nul.txt").
        string stem = name.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem)) return $"\"{stem}\" is a name Windows reserves. Choose another.";

        return null;
    }
}
