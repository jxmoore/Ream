using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>Makes repositories for tests. Everything sits directly under <c>root</c> (data folder "."), so tests can look at plain paths.</summary>
internal static class TestReam
{
    public const string FileName = "Test.ream";

    public static DocumentRepository Repo(string root) => new(Path.Combine(root, FileName), ReamPaths.SameFolder);

    /// <summary>The .ream file that <see cref="Repo"/> uses for this root.</summary>
    public static string FileIn(string root) => Path.Combine(root, FileName);
}
