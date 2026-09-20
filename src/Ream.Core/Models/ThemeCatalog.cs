namespace Ream.Core.Models;

/// <summary>One selectable color theme. <see cref="Id"/> is what config.json stores.</summary>
public sealed record ThemeInfo(string Id, string Name, bool IsLight)
{
    /// <summary>The palette file under Themes/: the id with a capital ("dracula" is Dracula.xaml).</summary>
    public string FileName => char.ToUpperInvariant(Id[0]) + Id[1..] + ".xaml";
}

public static class ThemeCatalog
{
    public const string DefaultId = "dark";

    public static IReadOnlyList<ThemeInfo> All { get; } =
    [
        new("dark", "Dark", IsLight: false),
        new("light", "Light", IsLight: true),
        new("dracula", "Dracula", IsLight: false),
        new("catppuccin", "Catppuccin Mocha", IsLight: false),
        new("material", "Material Palenight", IsLight: false),
        new("nord", "Nord", IsLight: false),
        new("gruvbox", "Gruvbox", IsLight: false),
    ];

    /// <summary>The theme for a config value; anything unrecognised (including the retired "system") is the dark default.</summary>
    public static ThemeInfo Resolve(string? id)
    {
        string wanted = id?.Trim() ?? "";
        return All.FirstOrDefault(t => string.Equals(t.Id, wanted, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
