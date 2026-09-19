namespace Ream.Core.Models;

public static class ThemeChoice
{
    /// <summary>Resolves the config's theme setting; anything unrecognised follows the system.</summary>
    public static bool IsLight(string? setting, bool systemIsLight) => setting?.Trim().ToLowerInvariant() switch
    {
        "light" => true,
        "dark" => false,
        _ => systemIsLight,
    };
}
