namespace Ream.Persistence;

public sealed record AppPaths(string ConfigFile, string DefaultDocumentsRoot)
{
    /// <param name="homeOverride">If given, config and documents both live under this folder (for dev and testing).</param>
    public static AppPaths Resolve(string? homeOverride = null)
    {
        if (homeOverride is not null)
        {
            return new AppPaths(
                Path.Combine(homeOverride, "config.json"),
                Path.Combine(homeOverride, "ReemDocuments"));
        }

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ream", "config.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ReemDocuments"));
    }
}
