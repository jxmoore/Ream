using System.Text.Json;
using Ream.Core.Models;
using Ream.Persistence;
using Ream.Persistence.Storage;

namespace Ream.App.Services;

/// <param name="Repository">The opened (or just created) ream's storage.</param>
/// <param name="Snapshot">What was loaded from it.</param>
/// <param name="Warning">Something worth telling the user about how it got here (the last ream would not open); null when all is well.</param>
internal sealed record LaunchResult(DocumentRepository Repository, DocumentSnapshot Snapshot, string? Warning);

/// <summary>
/// Decides which ream Ream starts with: the one open last time; failing that an old-format folder converted in place
/// (or a .ream already in it); failing that a brand-new ream (the tutorial, or an empty one if config says so).
/// </summary>
internal static class ReamLauncher
{
    public const string DefaultName = "My Ream";

    public static LaunchResult Launch(AppConfig config, AppPaths paths)
    {
        string? warning = null;

        if (!string.IsNullOrWhiteSpace(config.LastReam))
        {
            if (!File.Exists(config.LastReam))
                warning = $"The ream \"{config.LastReam}\" isn't there any more, so Ream started a new one.";
            else if (TryOpen(config.LastReam, out var opened, out string? error))
                return opened!;
            else
                warning = $"Ream couldn't open \"{config.LastReam}\" ({error}), so it started a new one.";
        }

        foreach (string folder in OldStyleFolders(config, paths))
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                string? reamFile = LegacyConverter.ConvertIfNeeded(folder)?.ReamFilePath
                    ?? Directory.EnumerateFiles(folder, "*" + ReamPaths.Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

                if (reamFile is not null)
                {
                    if (TryOpen(reamFile, out var opened, out string? error))
                        return opened! with { Warning = warning };
                    warning ??= $"Ream couldn't open \"{reamFile}\" ({error}), so it started a new one.";
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                warning ??= $"Ream couldn't convert the notes in \"{folder}\" ({ex.Message}), so it started a new ream.";
            }
        }

        return CreateNew(config, paths, warning);
    }

    /// <summary>Opens a ream file, reporting (not throwing) the usual ways that can fail.</summary>
    public static bool TryOpen(string reamFile, out LaunchResult? result, out string? error)
    {
        try
        {
            var repository = new DocumentRepository(reamFile);
            result = new LaunchResult(repository, repository.Load(), null);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Makes a new ream at <paramref name="reamFile"/> (which must be free) holding the tutorial, or nothing if config says so.</summary>
    public static LaunchResult Create(string reamFile, AppConfig config, string configFilePath, string? warning = null)
    {
        var repository = new DocumentRepository(reamFile);
        repository.Save(config.TutorialOnNew
            ? TutorialReam.Create(config, configFilePath, repository.ReamPath)
            : TutorialReam.Blank());
        return new LaunchResult(repository, repository.Load(), warning);
    }

    private static LaunchResult CreateNew(AppConfig config, AppPaths paths, string? warning)
    {
        Directory.CreateDirectory(paths.DefaultReamsFolder);
        string reamFile = ReamPaths.UniquePath(paths.DefaultReamsFolder, DefaultName);
        return Create(reamFile, config, paths.ConfigFile, warning);
    }

    private static IEnumerable<string> OldStyleFolders(AppConfig config, AppPaths paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? folder in new[] { config.DocumentsRoot, paths.DefaultDocumentsRoot })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            string full = Path.GetFullPath(folder);
            if (seen.Add(full)) yield return full;
        }
    }
}
