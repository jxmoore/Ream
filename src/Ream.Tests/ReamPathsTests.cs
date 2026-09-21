using Ream.Persistence.Storage;

namespace Ream.Tests;

public class ReamPathsTests
{
    [Theory]
    [InlineData("Foo.ream", true)]
    [InlineData(@"C:\x\Foo.ream", true)]
    [InlineData(@"C:\x\Foo.REAM", true)]
    [InlineData("My.Notes.ream", true)]
    [InlineData("Foo.reamlayout", false)]
    [InlineData("Foo.reamnote", false)]
    [InlineData("Foo.ream.tmp", false)]
    [InlineData("Foo.ream.corrupt-20260101000000", false)]
    [InlineData("Foo.txt", false)]
    [InlineData("Foo", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsReamFile_LooksAtTheExtensionOnly(string? path, bool expected)
    {
        Assert.Equal(expected, ReamPaths.IsReamFile(path));
    }

    [Theory]
    [InlineData("Foo.ream", "Foo")]
    [InlineData(@"C:\x\Foo.ream", "Foo")]
    [InlineData(@"C:\x y\My Notes.ream", "My Notes")]
    [InlineData("My.Notes.ream", "My.Notes")]
    public void NameOf_IsTheFileNameWithoutTheExtension(string path, string expected)
    {
        Assert.Equal(expected, ReamPaths.NameOf(path));
    }

    [Theory]
    [InlineData(@"C:\x\Foo.ream", "Foo")]
    [InlineData("My Notes.ream", "My Notes")]
    public void DefaultDataFolder_IsNamedAfterTheFile(string path, string expected)
    {
        Assert.Equal(expected, ReamPaths.DefaultDataFolder(path));
    }

    [Theory]
    [InlineData(".", true)]
    [InlineData("Foo", true)]
    [InlineData("Foo Bar", true)]
    [InlineData("data.d", true)]
    [InlineData("日本語", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("/abs", false)]
    [InlineData(@"C:\abs", false)]
    [InlineData(@"..\outside", false)]
    [InlineData("a:b", false)]
    [InlineData("a*b", false)]
    [InlineData("a?b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a<b", false)]
    [InlineData("a>b", false)]
    [InlineData("a|b", false)]
    [InlineData("a\tb", false)]
    public void IsValidDataFolder_AcceptsDotOrOnePlainFolderName(string? dataFolder, bool expected)
    {
        Assert.Equal(expected, ReamPaths.IsValidDataFolder(dataFolder));
    }

    [Fact]
    public void AFileCalledJustDotReam_IsNotAReamFile()
    {
        Assert.False(ReamPaths.IsReamFile(@"C:\x\.ream"));
        Assert.False(ReamPaths.IsReamFile(".ream"));
        Assert.True(ReamPaths.IsReamFile(@"C:\x\a.ream"));
    }

    [Fact]
    public void DataRootOf_IsBesideTheFile_OrTheFilesOwnFolderForDot()
    {
        using var dir = new TempDir();
        string file = dir.Combine("Foo.ream");

        Assert.Equal(dir.Combine("Foo"), ReamPaths.DataRootOf(file, "Foo"));
        Assert.Equal(dir.Combine("Elsewhere"), ReamPaths.DataRootOf(file, "Elsewhere"));
        Assert.Equal(dir.Path, ReamPaths.DataRootOf(file, "."));
    }

    [Fact]
    public void DataRootOf_ResolvesARelativeFilePath()
    {
        string root = ReamPaths.DataRootOf("Foo.ream", "Foo");

        Assert.True(Path.IsPathRooted(root));
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "Foo"), root);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData(@"..\..\x")]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    public void DataRootOf_ThrowsOnAnUnsafeDataFolder(string dataFolder)
    {
        using var dir = new TempDir();

        Assert.Throws<ArgumentException>(() => ReamPaths.DataRootOf(dir.Combine("Foo.ream"), dataFolder));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("My Notes")]
    [InlineData("Notes 2")]
    [InlineData("v1.2 draft")]
    [InlineData(".hidden")]
    [InlineData("Notizen für Übung")]
    [InlineData("日本語のメモ")]
    [InlineData("café")]
    [InlineData("COM10")]
    [InlineData("Console")]
    [InlineData("nully")]
    public void IsValidName_AcceptsOrdinaryNames(string name)
    {
        Assert.True(ReamPaths.IsValidName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData(" Foo")]
    [InlineData("Foo ")]
    [InlineData("Foo.")]
    [InlineData("Foo..")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    public void IsValidName_RejectsEmptySpacedDottedAndInvalidNames(string name)
    {
        Assert.False(ReamPaths.IsValidName(name));
    }

    [Fact]
    public void IsValidName_RejectsNull()
    {
        Assert.False(ReamPaths.IsValidName(null));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("Nul")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    [InlineData("nul.x")]
    [InlineData("CON.txt")]
    public void IsValidName_RejectsReservedDeviceNames(string name)
    {
        Assert.False(ReamPaths.IsValidName(name));
    }

    [Fact]
    public void IsValidName_LimitsTheLengthToOneHundred()
    {
        Assert.True(ReamPaths.IsValidName(new string('a', 100)));
        Assert.False(ReamPaths.IsValidName(new string('a', 101)));
        Assert.False(ReamPaths.IsValidName(new string('a', 300)));
    }

    [Fact]
    public void IsOccupied_IsFalseWhenNeitherTheFileNorItsDataFolderExists()
    {
        using var dir = new TempDir();

        Assert.False(ReamPaths.IsOccupied(dir.Combine("Foo.ream")));
    }

    [Fact]
    public void IsOccupied_IsTrueWhenTheFileExists()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("Foo.ream"), "{}");

        Assert.True(ReamPaths.IsOccupied(dir.Combine("Foo.ream")));
    }

    [Fact]
    public void IsOccupied_IsTrueWhenOnlyTheDataFolderExists()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Foo"));

        Assert.True(ReamPaths.IsOccupied(dir.Combine("Foo.ream")));
    }

    [Fact]
    public void IsOccupied_IgnoresOtherReamsInTheSameFolder()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("Other.ream"), "{}");
        Directory.CreateDirectory(dir.Combine("Other"));

        Assert.False(ReamPaths.IsOccupied(dir.Combine("Foo.ream")));
    }

    [Fact]
    public void UniquePath_UsesTheBareNameWhenFree()
    {
        using var dir = new TempDir();

        Assert.Equal(dir.Combine("Foo.ream"), ReamPaths.UniquePath(dir.Path, "Foo"));
    }

    [Fact]
    public void UniquePath_CountsUpWhenTheFileIsTaken()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("Foo.ream"), "{}");

        Assert.Equal(dir.Combine("Foo 2.ream"), ReamPaths.UniquePath(dir.Path, "Foo"));
    }

    [Fact]
    public void UniquePath_CountsUpWhenOnlyTheDataFolderIsTaken()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Foo"));

        Assert.Equal(dir.Combine("Foo 2.ream"), ReamPaths.UniquePath(dir.Path, "Foo"));
    }

    [Fact]
    public void UniquePath_KeepsCountingPastEverythingTaken()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("Foo.ream"), "{}");
        Directory.CreateDirectory(dir.Combine("Foo 2"));
        File.WriteAllText(dir.Combine("Foo 3.ream"), "{}");

        Assert.Equal(dir.Combine("Foo 4.ream"), ReamPaths.UniquePath(dir.Path, "Foo"));
    }
}
