namespace Ream.Persistence;

/// <param name="ConfigFile">config.json.</param>
/// <param name="DefaultDocumentsRoot">Where the old, pre-.ream single document lived; only used to find and convert it.</param>
/// <param name="DefaultReamsFolder">Where a brand-new ream is created when there is no last ream to reopen.</param>
public sealed record AppPaths(string ConfigFile, string DefaultDocumentsRoot, string DefaultReamsFolder)
{
    /// <param name="homeOverride">If given, config and reams both live under this folder (for dev and testing).</param>
    public static AppPaths Resolve(string? homeOverride = null)
    {
        if (homeOverride is not null)
        {
            return new AppPaths(
                Path.Combine(homeOverride, "config.json"),
                Path.Combine(homeOverride, "ReemDocuments"),
                Path.Combine(homeOverride, "Reams"));
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ream", "config.json"),
            Path.Combine(documents, "ReemDocuments"),
            Path.Combine(documents, "Ream"));
    }
}
