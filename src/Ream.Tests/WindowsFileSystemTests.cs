using Ream.App.Services.FileBrowser;

namespace Ream.Tests;

/// <summary>The real <see cref="WindowsFileSystem"/>, only ever pointed at a temp folder.</summary>
public class WindowsFileSystemTests
{
    private readonly WindowsFileSystem _fs = new();

    [Fact]
    public void List_returns_files_and_folders_with_size_and_date()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("Folder"));
        File.WriteAllText(temp.Combine("a.ream"), "12345");
        var stamp = new DateTime(2025, 6, 7, 8, 9, 10);
        File.SetLastWriteTime(temp.Combine("a.ream"), stamp);

        var entries = _fs.List(temp.Path).OrderBy(e => e.Name).ToList();

        Assert.Equal(new[] { "a.ream", "Folder" }, entries.Select(e => e.Name));
        Assert.Equal(temp.Combine("a.ream"), entries[0].FullPath);
        Assert.False(entries[0].IsDirectory);
        Assert.Equal(5, entries[0].Size);
        Assert.Equal(stamp, entries[0].Modified);
        Assert.True(entries[1].IsDirectory);
        Assert.Equal(0, entries[1].Size);
    }

    [Fact]
    public void List_leaves_out_hidden_and_system_items()
    {
        using var temp = new TempDir();
        File.WriteAllText(temp.Combine("shown.ream"), "");
        File.WriteAllText(temp.Combine("hidden.ream"), "");
        File.WriteAllText(temp.Combine("system.ream"), "");
        Directory.CreateDirectory(temp.Combine("hiddenDir"));
        File.SetAttributes(temp.Combine("hidden.ream"), FileAttributes.Hidden);
        File.SetAttributes(temp.Combine("system.ream"), FileAttributes.System);
        File.SetAttributes(temp.Combine("hiddenDir"), FileAttributes.Hidden);

        var names = _fs.List(temp.Path).Select(e => e.Name).ToList();

        Assert.Equal(new[] { "shown.ream" }, names);
    }

    [Fact]
    public void List_of_a_missing_folder_throws_a_directory_not_found()
    {
        using var temp = new TempDir();

        Assert.Throws<DirectoryNotFoundException>(() => _fs.List(temp.Combine("nope")));
    }

    [Fact]
    public void Exists_checks_tell_files_from_folders()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("Folder"));
        File.WriteAllText(temp.Combine("a.ream"), "");

        Assert.True(_fs.DirectoryExists(temp.Combine("Folder")));
        Assert.False(_fs.DirectoryExists(temp.Combine("a.ream")));
        Assert.True(_fs.FileExists(temp.Combine("a.ream")));
        Assert.False(_fs.FileExists(temp.Combine("Folder")));
        Assert.False(_fs.FileExists(temp.Combine("missing")));
    }

    [Fact]
    public void ParentOf_walks_up_and_is_null_at_a_drive_root()
    {
        Assert.Equal(@"C:\a", _fs.ParentOf(@"C:\a\b"));
        Assert.Equal(@"C:\a", _fs.ParentOf(@"C:\a\b\"));
        Assert.Equal(@"C:\", _fs.ParentOf(@"C:\a"));
        Assert.Null(_fs.ParentOf(@"C:\"));
    }

    [Fact]
    public void CreateDirectory_makes_the_folder_and_Combine_joins_paths()
    {
        using var temp = new TempDir();
        string path = _fs.Combine(temp.Path, "New");

        _fs.CreateDirectory(path);

        Assert.Equal(temp.Combine("New"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Places_are_existing_folders_in_the_two_groups_without_duplicates()
    {
        var places = _fs.Places();

        Assert.All(places, p =>
        {
            Assert.True(Directory.Exists(p.Path), p.Path);
            Assert.Contains(p.Group, new[] { WindowsFileSystem.QuickAccess, WindowsFileSystem.ThisPc });
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
        });
        Assert.Equal(places.Count, places.Select(p => p.Path.ToUpperInvariant()).Distinct().Count());
        Assert.Contains(places, p => p.Group == WindowsFileSystem.ThisPc);
        Assert.All(places.Where(p => p.Group == WindowsFileSystem.ThisPc), p => Assert.Matches(@"\(.:\)$", p.Name));

        int firstDrive = places.ToList().FindIndex(p => p.Group == WindowsFileSystem.ThisPc);
        Assert.All(places.Take(firstDrive), p => Assert.Equal(WindowsFileSystem.QuickAccess, p.Group));
    }

    [Fact]
    public void The_model_works_over_the_real_disk_in_a_temp_folder()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("Data"));
        Directory.CreateDirectory(temp.Combine("Foo"));
        File.WriteAllText(temp.Combine("Foo.ream"), "{}");
        File.WriteAllText(temp.Combine("notes.txt"), "");
        File.WriteAllText(temp.Combine("hidden.ream"), "{}");
        File.SetAttributes(temp.Combine("hidden.ream"), FileAttributes.Hidden);

        var open = new FileBrowserModel(_fs, BrowserMode.Open, temp.Path);
        Assert.Equal(new[] { "Data", "Foo", "Foo.ream" }, open.Entries.Select(e => e.Name));
        open.FileName = "Foo";
        Assert.Equal(temp.Combine("Foo.ream"), open.TryAccept().Path);

        var save = new FileBrowserModel(_fs, BrowserMode.Save, temp.Path, "Foo");
        Assert.Equal(AcceptOutcome.NavigatedInstead, save.TryAccept().Outcome);

        save.Navigate(temp.Path);
        save.FileName = "Foo.ream";
        Assert.Equal(AcceptOutcome.Rejected, save.TryAccept().Outcome);

        save.FileName = "Bar";
        Assert.Equal(temp.Combine("Bar.ream"), save.TryAccept().Path);

        Assert.True(save.CreateFolder("Made", out _));
        Assert.True(Directory.Exists(temp.Combine("Made")));
        Assert.Equal("Made", save.Selected?.Name);

        save.Navigate(temp.Combine("Made"));
        Assert.Equal(new[] { temp.Path, temp.Combine("Made") }, save.Breadcrumbs.Skip(save.Breadcrumbs.Count - 2).Select(b => b.Path));
        save.Up();
        Assert.Equal(temp.Path, save.CurrentDirectory);
    }
}
