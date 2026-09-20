using System.Reflection;
using System.Runtime.InteropServices;

namespace Ream.App.Services;

/// <summary>What the About window shows.</summary>
internal sealed record AboutInfo(
    string Name,
    string Version,
    string Runtime,
    string System,
    string DocumentsFolder,
    string ConfigFile,
    string Repository)
{
    public const string RepositoryUrl = "https://github.com/jxmoore/Ream";

    /// <param name="documentsFolder">Where the notes live; unknown (for example in tests) shows a dash.</param>
    /// <param name="configFile">Where config.json lives; unknown shows a dash.</param>
    public static AboutInfo Create(string? documentsFolder = null, string? configFile = null) => new(
        "Ream",
        VersionOf(typeof(AboutInfo).Assembly),
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription,
        string.IsNullOrWhiteSpace(documentsFolder) ? "-" : documentsFolder,
        string.IsNullOrWhiteSpace(configFile) ? "-" : configFile,
        RepositoryUrl);

    /// <summary>The version people know it by: "0.5.0", without the build metadata after a "+".</summary>
    public static string VersionOf(Assembly assembly)
    {
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informational ?? assembly.GetName().Version?.ToString() ?? "unknown";

        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
