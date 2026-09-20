using System.ComponentModel;
using System.Diagnostics;
using Ream.App.Services.FileBrowser;

namespace Ream.Tests;

public class FileBrowserModelTests
{
    private const string Docs = @"C:\Users\Joe\Documents";
    private const string Desktop = @"C:\Users\Joe\Desktop";

    private static readonly DateTime Jan = new(2026, 1, 1);
    private static readonly DateTime Feb = new(2026, 2, 1);
    private static readonly DateTime Mar = new(2026, 3, 1);

    /// <summary>C:\Users\Joe\Documents holds two reams (with their data folders), a plain folder and two other files.</summary>
    private static FakeFileSystem Disk()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(Desktop)
            .AddDirectory(@"D:\")
            .AddFile(Docs + @"\Notes.ream")
            .AddDirectory(Docs + @"\Notes")
            .AddFile(Docs + @"\Work.ream")
            .AddDirectory(Docs + @"\Work")
            .AddDirectory(Docs + @"\Sub")
            .AddFile(Docs + @"\Sub\Inner.ream")
            .AddFile(Docs + @"\readme.txt")
            .AddFile(Docs + @"\LICENSE")
            .AddPlace("Desktop", Desktop, "Quick access")
            .AddPlace("Documents", Docs, "Quick access")
            .AddPlace("Local Disk (C:)", @"C:\", "This PC")
            .AddPlace("Local Disk (D:)", @"D:\", "This PC");
        return fs;
    }

    private static FileBrowserModel OpenMode(IFileSystem fs, string directory = Docs) => new(fs, BrowserMode.Open, directory);

    private static FileBrowserModel SaveMode(IFileSystem fs, string directory = Docs, string? suggested = null) =>
        new(fs, BrowserMode.Save, directory, suggested);

    private static string[] Names(FileBrowserModel model) => model.Entries.Select(e => e.Name).ToArray();

    private static List<string> Watch(FileBrowserModel model)
    {
        var raised = new List<string>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        return raised;
    }

    // ----- Where it starts -----

    [Fact]
    public void Starts_in_the_initial_directory_when_it_exists()
    {
        var model = OpenMode(Disk(), Desktop);

        Assert.Equal(Desktop, model.CurrentDirectory);
        Assert.Null(model.ListingError);
        Assert.False(model.CanGoBack);
        Assert.False(model.CanGoForward);
    }

    [Fact]
    public void A_trailing_separator_on_the_initial_directory_is_dropped()
    {
        var model = OpenMode(Disk(), Docs + @"\");

        Assert.Equal(Docs, model.CurrentDirectory);
    }

    [Fact]
    public void A_missing_initial_directory_falls_back_to_the_first_quick_access_place_that_exists()
    {
        var fs = Disk();
        fs.PlaceList.Insert(0, new BrowserPlace("Gone", @"C:\Users\Joe\Gone", "Quick access"));

        var model = OpenMode(fs, @"C:\Nowhere\At\All");

        Assert.Equal(Desktop, model.CurrentDirectory);
    }

    [Fact]
    public void A_missing_initial_directory_falls_back_to_the_first_drive_when_no_quick_access_place_exists()
    {
        var fs = Disk();
        fs.PlaceList.Clear();
        fs.AddPlace("Gone", @"C:\Gone", "Quick access").AddPlace("Data (D:)", @"D:\", "This PC").AddPlace("Local Disk (C:)", @"C:\", "This PC");

        var model = OpenMode(fs, @"C:\Nowhere");

        Assert.Equal(@"D:\", model.CurrentDirectory);
    }

    [Fact]
    public void An_empty_initial_directory_also_falls_back()
    {
        var model = OpenMode(Disk(), "");

        Assert.Equal(Desktop, model.CurrentDirectory);
    }

    [Fact]
    public void With_nothing_to_fall_back_to_the_folder_is_reported_missing()
    {
        var fs = new FakeFileSystem();

        var model = OpenMode(fs, @"C:\Nowhere");

        Assert.Equal(@"C:\Nowhere", model.CurrentDirectory);
        Assert.NotNull(model.ListingError);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void Places_come_from_the_file_system()
    {
        var model = OpenMode(Disk());

        Assert.Equal(new[] { "Desktop", "Documents", "Local Disk (C:)", "Local Disk (D:)" }, model.Places.Select(p => p.Name));
        Assert.Equal("This PC", model.Places[2].Group);
    }

    [Fact]
    public void Refresh_reads_the_places_again()
    {
        var fs = Disk();
        var model = OpenMode(fs);
        fs.AddPlace("Local Disk (E:)", @"E:\", "This PC");

        model.Refresh();

        Assert.Equal(5, model.Places.Count);
    }

    // ----- Sorting -----

    private static FileBrowserModel Sortable(out FakeFileSystem fs)
    {
        fs = new FakeFileSystem();
        fs.AddDirectory(@"C:\S\zeta", Jan).AddDirectory(@"C:\S\Alpha", Mar).AddDirectory(@"C:\S\mid", Feb)
            .AddFile(@"C:\S\b.ream", modified: Jan)
            .AddFile(@"C:\S\A.ream", modified: Mar)
            .AddFile(@"C:\S\file10.ream", modified: Feb)
            .AddFile(@"C:\S\file2.ream", modified: new DateTime(2026, 4, 1));
        return OpenMode(fs, @"C:\S");
    }

    [Fact]
    public void Folders_come_first_then_files_by_name_ignoring_case_with_numbers_in_order()
    {
        var model = Sortable(out _);

        Assert.Equal(new[] { "Alpha", "mid", "zeta", "A.ream", "b.ream", "file2.ream", "file10.ream" }, Names(model));
    }

    [Fact]
    public void Sorting_by_name_descending_reverses_each_group_but_keeps_folders_first()
    {
        var model = Sortable(out _);

        model.SortDescending = true;

        Assert.Equal(new[] { "zeta", "mid", "Alpha", "file10.ream", "file2.ream", "b.ream", "A.ream" }, Names(model));
    }

    [Fact]
    public void Sorting_by_modified_puts_the_newest_first_with_folders_first()
    {
        var model = Sortable(out _);

        model.SortBy = SortColumn.Modified;

        Assert.Equal(new[] { "Alpha", "mid", "zeta", "file2.ream", "A.ream", "file10.ream", "b.ream" }, Names(model));
    }

    [Fact]
    public void Sorting_by_modified_descending_puts_the_oldest_first()
    {
        var model = Sortable(out _);

        model.SortBy = SortColumn.Modified;
        model.SortDescending = true;

        Assert.Equal(new[] { "zeta", "mid", "Alpha", "b.ream", "file10.ream", "A.ream", "file2.ream" }, Names(model));
    }

    [Fact]
    public void Equal_modified_dates_fall_back_to_the_name()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\T\c.ream", modified: Jan).AddFile(@"C:\T\a.ream", modified: Jan).AddFile(@"C:\T\b.ream", modified: Jan);
        var model = OpenMode(fs, @"C:\T");

        model.SortBy = SortColumn.Modified;

        Assert.Equal(new[] { "a.ream", "b.ream", "c.ream" }, Names(model));
    }

    [Fact]
    public void Changing_the_sort_relists_without_touching_history_or_the_disk()
    {
        var model = Sortable(out var fs);
        int listed = fs.ListCount;

        model.SortBy = SortColumn.Modified;
        model.SortDescending = true;
        model.ShowAllFiles = true;

        Assert.Equal(listed, fs.ListCount);
        Assert.False(model.CanGoBack);
        Assert.False(model.CanGoForward);
    }

    [Fact]
    public void SortOn_flips_the_direction_of_the_same_column_and_resets_it_for_another()
    {
        var model = Sortable(out _);

        model.SortOn(SortColumn.Name);
        Assert.True(model.SortDescending);

        model.SortOn(SortColumn.Modified);
        Assert.Equal(SortColumn.Modified, model.SortBy);
        Assert.False(model.SortDescending);

        model.SortOn(SortColumn.Modified);
        Assert.True(model.SortDescending);
    }

    [Fact]
    public void Natural_compare_orders_numbers_by_value_and_ignores_case()
    {
        Assert.True(FileBrowserModel.NaturalCompare("note 2", "note 10") < 0);
        Assert.True(FileBrowserModel.NaturalCompare("Note", "note") != 0);
        Assert.True(FileBrowserModel.NaturalCompare("apple", "Banana") < 0);
        Assert.True(FileBrowserModel.NaturalCompare("a007", "a7") != 0);
        Assert.True(FileBrowserModel.NaturalCompare("a", "ab") < 0);
        Assert.Equal(0, FileBrowserModel.NaturalCompare("same", "same"));
    }

    // ----- Filter and hidden items -----

    [Fact]
    public void Only_ream_files_and_folders_are_listed_by_default()
    {
        var model = OpenMode(Disk());

        Assert.False(model.ShowAllFiles);
        Assert.Equal(new[] { "Notes", "Sub", "Work", "Notes.ream", "Work.ream" }, Names(model));
    }

    [Fact]
    public void All_files_lists_everything_and_switching_back_narrows_again()
    {
        var model = OpenMode(Disk());

        model.ShowAllFiles = true;
        Assert.Equal(new[] { "Notes", "Sub", "Work", "LICENSE", "Notes.ream", "readme.txt", "Work.ream" }, Names(model));

        model.ShowAllFiles = false;
        Assert.Equal(5, model.Entries.Count);
    }

    [Fact]
    public void The_ream_filter_ignores_case_and_needs_a_name_before_the_extension()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\T\Shout.REAM").AddFile(@"C:\T\.ream").AddFile(@"C:\T\notream.reamx");

        var model = OpenMode(fs, @"C:\T");

        Assert.Equal(new[] { "Shout.REAM" }, Names(model));
    }

    [Fact]
    public void Hidden_items_are_not_listed_even_with_all_files()
    {
        var fs = Disk();
        fs.AddFile(Docs + @"\secret.ream", hidden: true).AddDirectory(Docs + @"\.git", hidden: true);
        var model = OpenMode(fs);

        model.ShowAllFiles = true;

        Assert.DoesNotContain("secret.ream", Names(model));
        Assert.DoesNotContain(".git", Names(model));
    }

    [Fact]
    public void Entries_carry_the_size_and_date()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\T\a.ream", 1234, Feb);
        var entry = OpenMode(fs, @"C:\T").Entries.Single();

        Assert.False(entry.IsDirectory);
        Assert.Equal(1234, entry.Size);
        Assert.Equal(Feb, entry.Modified);
        Assert.Equal(@"C:\T\a.ream", entry.FullPath);
    }

    // ----- Selection and the name box -----

    [Fact]
    public void Selecting_a_file_copies_its_name_with_extension_into_the_name_box()
    {
        var model = OpenMode(Disk());

        model.Selected = model.Entries.First(e => e.Name == "Notes.ream");

        Assert.Equal("Notes.ream", model.FileName);
    }

    [Fact]
    public void Selecting_a_folder_leaves_the_name_box_alone()
    {
        var model = SaveMode(Disk(), suggested: "Draft");

        model.Selected = model.Entries.First(e => e.Name == "Sub");

        Assert.Equal("Draft", model.FileName);
        Assert.Equal("Sub", model.Selected!.Name);
    }

    [Fact]
    public void Navigating_clears_the_selection_and_a_name_that_came_from_it()
    {
        var model = OpenMode(Disk());
        model.Selected = model.Entries.First(e => e.Name == "Notes.ream");

        model.Navigate(Desktop);

        Assert.Null(model.Selected);
        Assert.Equal("", model.FileName);
    }

    [Fact]
    public void Navigating_keeps_a_name_the_user_typed()
    {
        var model = SaveMode(Disk(), suggested: "Draft");

        model.Navigate(Desktop);

        Assert.Equal("Draft", model.FileName);
    }

    [Fact]
    public void A_suggested_name_seeds_the_name_box_in_save_mode_and_selects_nothing_even_if_it_exists()
    {
        var model = SaveMode(Disk(), suggested: "Notes.ream");

        Assert.Equal("Notes.ream", model.FileName);
        Assert.Null(model.Selected);
    }

    [Fact]
    public void A_suggested_name_is_ignored_in_open_mode()
    {
        var model = new FileBrowserModel(Disk(), BrowserMode.Open, Docs, "Untitled");

        Assert.Equal("", model.FileName);
    }

    [Fact]
    public void Refresh_keeps_the_selection_while_the_item_is_still_there_and_drops_it_when_it_is_gone()
    {
        var fs = Disk();
        var model = OpenMode(fs);
        model.Selected = model.Entries.First(e => e.Name == "Work.ream");

        model.Refresh();
        Assert.Equal("Work.ream", model.Selected?.Name);

        fs.Remove(Docs + @"\Work.ream");
        model.Refresh();
        Assert.Null(model.Selected);
        Assert.DoesNotContain("Work.ream", Names(model));
    }

    // ----- Navigate / Back / Forward / Up -----

    [Fact]
    public void Navigate_goes_to_an_existing_folder_and_records_history()
    {
        var model = OpenMode(Disk());

        Assert.True(model.Navigate(Desktop));

        Assert.Equal(Desktop, model.CurrentDirectory);
        Assert.True(model.CanGoBack);
        Assert.False(model.CanGoForward);
    }

    [Fact]
    public void Navigate_to_a_missing_folder_returns_false_and_changes_nothing()
    {
        var model = OpenMode(Disk());
        model.Selected = model.Entries.First(e => e.Name == "Notes.ream");

        Assert.False(model.Navigate(@"C:\Nope"));
        Assert.False(model.Navigate(Docs + @"\Notes.ream"));
        Assert.False(model.Navigate(""));

        Assert.Equal(Docs, model.CurrentDirectory);
        Assert.False(model.CanGoBack);
        Assert.NotNull(model.Selected);
    }

    [Fact]
    public void Navigate_normalises_the_path()
    {
        var model = OpenMode(Disk());

        model.Navigate(Docs + @"\Sub\..\..\Desktop\");

        Assert.Equal(Desktop, model.CurrentDirectory);
    }

    [Fact]
    public void Navigating_to_the_folder_you_are_in_adds_no_history()
    {
        var model = OpenMode(Disk());

        Assert.True(model.Navigate(Docs));

        Assert.False(model.CanGoBack);
    }

    [Fact]
    public void Back_and_Forward_walk_the_history()
    {
        var model = OpenMode(Disk());
        model.Navigate(Desktop);
        model.Navigate(@"C:\Users");

        model.Back();
        Assert.Equal(Desktop, model.CurrentDirectory);
        Assert.True(model.CanGoBack);
        Assert.True(model.CanGoForward);

        model.Back();
        Assert.Equal(Docs, model.CurrentDirectory);
        Assert.False(model.CanGoBack);

        model.Back();
        Assert.Equal(Docs, model.CurrentDirectory);

        model.Forward();
        model.Forward();
        Assert.Equal(@"C:\Users", model.CurrentDirectory);
        Assert.False(model.CanGoForward);

        model.Forward();
        Assert.Equal(@"C:\Users", model.CurrentDirectory);
    }

    [Fact]
    public void Navigating_after_Back_drops_the_forward_history()
    {
        var model = OpenMode(Disk());
        model.Navigate(Desktop);
        model.Navigate(@"C:\Users");
        model.Back();
        model.Back();

        model.Navigate(Docs + @"\Sub");

        Assert.False(model.CanGoForward);
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
        model.Back();
        Assert.Equal(Docs, model.CurrentDirectory);
        model.Forward();
        model.Forward();
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
    }

    [Fact]
    public void Back_and_Forward_list_the_folder_again()
    {
        var fs = Disk();
        var model = OpenMode(fs);
        model.Navigate(Desktop);
        fs.AddFile(Docs + @"\New.ream");

        model.Back();

        Assert.Contains("New.ream", Names(model));
    }

    [Fact]
    public void Up_goes_to_the_parent_and_can_be_undone_with_Back()
    {
        var model = OpenMode(Disk());

        Assert.True(model.CanGoUp);
        model.Up();

        Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);
        model.Back();
        Assert.Equal(Docs, model.CurrentDirectory);
    }

    [Fact]
    public void Up_from_a_drive_root_does_nothing()
    {
        var model = OpenMode(Disk(), @"C:\");

        Assert.False(model.CanGoUp);
        model.Up();

        Assert.Equal(@"C:\", model.CurrentDirectory);
        Assert.False(model.CanGoBack);
    }

    [Fact]
    public void CanGoUp_follows_the_folder()
    {
        var model = OpenMode(Disk(), @"C:\");
        Assert.False(model.CanGoUp);

        model.Navigate(@"C:\Users");
        Assert.True(model.CanGoUp);

        model.Up();
        Assert.Equal(@"C:\", model.CurrentDirectory);
        Assert.False(model.CanGoUp);
    }

    [Fact]
    public void Going_back_to_a_folder_that_has_vanished_reports_it_and_Up_still_works()
    {
        var fs = Disk();
        var model = OpenMode(fs);
        model.Navigate(Desktop);
        fs.Remove(Docs);

        model.Back();

        Assert.NotNull(model.ListingError);
        Assert.Empty(model.Entries);
        model.Up();
        Assert.Null(model.ListingError);
        Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);
    }

    // ----- Breadcrumbs -----

    [Fact]
    public void Breadcrumbs_run_from_the_drive_root_down_to_the_current_folder()
    {
        var model = OpenMode(Disk());

        Assert.Equal(new[] { @"C:\", "Users", "Joe", "Documents" }, model.Breadcrumbs.Select(b => b.Name));
        Assert.Equal(new[] { @"C:\", @"C:\Users", @"C:\Users\Joe", Docs }, model.Breadcrumbs.Select(b => b.Path));
    }

    [Fact]
    public void Breadcrumbs_at_a_drive_root_are_just_the_root()
    {
        var model = OpenMode(Disk(), @"D:\");

        Assert.Equal(new[] { new Breadcrumb(@"D:\", @"D:\") }, model.Breadcrumbs);
    }

    [Fact]
    public void Breadcrumbs_follow_navigation()
    {
        var model = OpenMode(Disk());

        model.Navigate(Docs + @"\Sub");

        Assert.Equal("Sub", model.Breadcrumbs[^1].Name);
        Assert.Equal(5, model.Breadcrumbs.Count);
    }

    // ----- The address box -----

    [Fact]
    public void Address_text_naming_a_folder_navigates_there()
    {
        var model = OpenMode(Disk());

        var result = model.NavigateToText(Desktop);

        Assert.Equal(NavigateOutcome.Navigated, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(Desktop, model.CurrentDirectory);
    }

    [Fact]
    public void Address_text_naming_a_file_goes_to_its_folder_and_selects_it()
    {
        var model = OpenMode(Disk(), Desktop);

        var result = model.NavigateToText(Docs + @"\Work.ream");

        Assert.Equal(NavigateOutcome.SelectedFile, result.Outcome);
        Assert.Equal(Docs, model.CurrentDirectory);
        Assert.Equal("Work.ream", model.Selected?.Name);
        Assert.Equal("Work.ream", model.FileName);
    }

    [Fact]
    public void Address_text_naming_a_file_the_filter_hides_still_selects_it()
    {
        var model = OpenMode(Disk(), Desktop);

        var result = model.NavigateToText(Docs + @"\readme.txt");

        Assert.Equal(NavigateOutcome.SelectedFile, result.Outcome);
        Assert.Equal("readme.txt", model.Selected?.Name);
        Assert.Equal(10, model.Selected!.Size);
    }

    [Fact]
    public void Address_text_expands_environment_variables()
    {
        const string variable = "REAM_FB_TEST_ADDRESS";
        Environment.SetEnvironmentVariable(variable, Docs);
        try
        {
            var model = OpenMode(Disk(), Desktop);

            var result = model.NavigateToText($@"%{variable}%\Sub");

            Assert.Equal(NavigateOutcome.Navigated, result.Outcome);
            Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Address_text_may_be_quoted_and_padded()
    {
        var model = OpenMode(Disk(), Desktop);

        var result = model.NavigateToText($"  \"{Docs}\"  ");

        Assert.Equal(NavigateOutcome.Navigated, result.Outcome);
        Assert.Equal(Docs, model.CurrentDirectory);
    }

    [Fact]
    public void Address_text_may_be_relative_to_the_current_folder()
    {
        var model = OpenMode(Disk());

        Assert.Equal(NavigateOutcome.Navigated, model.NavigateToText("Sub").Outcome);
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
    }

    [Fact]
    public void A_bare_drive_letter_goes_to_the_drive_root()
    {
        var model = OpenMode(Disk());

        Assert.Equal(NavigateOutcome.Navigated, model.NavigateToText("D:").Outcome);
        Assert.Equal(@"D:\", model.CurrentDirectory);
    }

    [Theory]
    [InlineData(@"C:\Not\A\Place")]
    [InlineData("nothing here")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("C:\\bad\u0000path")]
    public void Address_text_that_is_not_a_place_is_invalid_with_a_message_and_changes_nothing(string text)
    {
        var model = OpenMode(Disk());

        var result = model.NavigateToText(text);

        Assert.Equal(NavigateOutcome.Invalid, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(Docs, model.CurrentDirectory);
        Assert.False(model.CanGoBack);
    }

    // ----- Folders that cannot be read -----

    [Fact]
    public void A_folder_that_cannot_be_read_sets_ListingError_and_empties_the_list()
    {
        var fs = Disk();
        fs.Deny(Docs + @"\Sub");
        var model = OpenMode(fs);

        model.Navigate(Docs + @"\Sub");

        Assert.NotNull(model.ListingError);
        Assert.Empty(model.Entries);
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
    }

    [Fact]
    public void The_next_navigation_clears_the_error()
    {
        var fs = Disk();
        fs.Deny(Docs + @"\Sub");
        var model = OpenMode(fs);
        model.Navigate(Docs + @"\Sub");

        model.Up();

        Assert.Null(model.ListingError);
        Assert.NotEmpty(model.Entries);
    }

    [Fact]
    public void Back_out_of_a_denied_folder_clears_the_error()
    {
        var fs = Disk();
        fs.Deny(Docs + @"\Sub");
        var model = OpenMode(fs);
        model.Navigate(Docs + @"\Sub");

        model.Back();

        Assert.Null(model.ListingError);
    }

    [Fact]
    public void Starting_in_a_denied_folder_reports_it()
    {
        var fs = Disk();
        fs.Deny(Docs);

        var model = OpenMode(fs);

        Assert.NotNull(model.ListingError);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void Refresh_picks_up_a_folder_that_became_readable()
    {
        var fs = Disk();
        fs.Deny(Docs);
        var model = OpenMode(fs);
        fs.Allow(Docs);

        model.Refresh();

        Assert.Null(model.ListingError);
        Assert.NotEmpty(model.Entries);
    }

    [Fact]
    public void An_io_error_while_listing_is_reported_too()
    {
        var fs = new ThrowingFileSystem(new IOException("The network path was not found."));

        var model = OpenMode(fs, @"C:\");

        Assert.Contains("network path", model.ListingError);
        Assert.Empty(model.Entries);
    }

    private sealed class ThrowingFileSystem(Exception error) : IFileSystem
    {
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
        public IReadOnlyList<FileEntry> List(string directory) => throw error;
        public IReadOnlyList<BrowserPlace> Places() => [];
        public string? ParentOf(string path) => null;
        public string Combine(string directory, string name) => Path.Combine(directory, name);
        public void CreateDirectory(string path) => throw error;
    }

    // ----- New folder -----

    [Fact]
    public void CreateFolder_makes_the_folder_lists_it_and_selects_it()
    {
        var fs = Disk();
        var model = OpenMode(fs);

        bool ok = model.CreateFolder("Fresh", out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Contains(fs.Calls, c => c == @"CreateDirectory:C:\Users\Joe\Documents\Fresh");
        Assert.Equal(Docs, model.CurrentDirectory);
        Assert.Equal("Fresh", model.Selected?.Name);
        Assert.True(model.Selected!.IsDirectory);
        Assert.Contains("Fresh", Names(model));
        Assert.Equal("", model.FileName);
    }

    [Fact]
    public void CreateFolder_trims_the_name()
    {
        var model = OpenMode(Disk());

        Assert.True(model.CreateFolder("  Spaced  ", out _));

        Assert.Equal("Spaced", model.Selected?.Name);
    }

    [Theory]
    [InlineData("Notes")]
    [InlineData("notes")]
    [InlineData("LICENSE")]
    public void CreateFolder_refuses_a_name_that_already_exists(string name)
    {
        var fs = Disk();
        var model = OpenMode(fs);

        bool ok = model.CreateFolder(name, out string? error);

        Assert.False(ok);
        Assert.Contains("already", error);
        Assert.DoesNotContain(fs.Calls, c => c.StartsWith("CreateDirectory:"));
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("a*b")]
    [InlineData("a:b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a|b")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dots.")]
    public void CreateFolder_refuses_invalid_names_with_a_message(string name)
    {
        var fs = Disk();
        var model = OpenMode(fs);

        bool ok = model.CreateFolder(name, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain(fs.Calls, c => c.StartsWith("CreateDirectory:"));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("lpt9.txt")]
    public void CreateFolder_refuses_reserved_names(string name)
    {
        var model = OpenMode(Disk());

        bool ok = model.CreateFolder(name, out string? error);

        Assert.False(ok);
        Assert.Contains("reserves", error);
    }

    [Fact]
    public void CreateFolder_refuses_a_name_that_is_too_long()
    {
        var model = OpenMode(Disk());

        Assert.False(model.CreateFolder(new string('x', 256), out string? error));
        Assert.Contains("shorter", error);
    }

    [Fact]
    public void CreateFolder_reports_an_access_denied_and_an_io_error()
    {
        var fs = Disk();
        var model = OpenMode(fs);

        fs.CreateDirectoryError = new UnauthorizedAccessException();
        Assert.False(model.CreateFolder("A", out string? denied));
        Assert.Contains("allowed", denied);

        fs.CreateDirectoryError = new IOException("The disk is full.");
        Assert.False(model.CreateFolder("B", out string? io));
        Assert.Contains("disk is full", io);
        Assert.DoesNotContain("A", Names(model));
    }

    [Fact]
    public void CreateFolder_in_a_folder_that_has_vanished_fails()
    {
        var fs = Disk();
        var model = OpenMode(fs);
        fs.Remove(Docs);

        Assert.False(model.CreateFolder("A", out string? error));
        Assert.Contains("doesn't exist", error);
    }

    // ----- Accept: Open -----

    [Fact]
    public void Open_accepts_an_existing_ream_by_name_relative_to_the_current_folder()
    {
        var model = OpenMode(Disk());
        model.FileName = "Work.ream";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Work.ream", result.Path);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Open_adds_the_extension_when_the_name_has_none_and_that_ream_exists()
    {
        var model = OpenMode(Disk());
        model.FileName = "Work";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Work.ream", result.Path);
    }

    [Fact]
    public void Open_accepts_a_full_path_and_ignores_case_quotes_and_spaces()
    {
        var model = OpenMode(Disk(), Desktop);
        model.FileName = $"  \"{Docs.ToUpperInvariant()}\\NOTES.REAM\"  ";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs.ToUpperInvariant() + @"\NOTES.REAM", result.Path, ignoreCase: true);
    }

    [Fact]
    public void Open_expands_environment_variables_in_the_name()
    {
        const string variable = "REAM_FB_TEST_OPEN";
        Environment.SetEnvironmentVariable(variable, Docs);
        try
        {
            var model = OpenMode(Disk(), Desktop);
            model.FileName = $@"%{variable}%\Notes.ream";

            var result = model.TryAccept();

            Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
            Assert.Equal(Docs + @"\Notes.ream", result.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Open_accepts_a_ream_in_a_sub_folder_by_relative_path()
    {
        var model = OpenMode(Disk());
        model.FileName = @"Sub\Inner";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Sub\Inner.ream", result.Path);
    }

    [Fact]
    public void Open_prefers_the_ream_over_its_data_folder_of_the_same_name()
    {
        var model = OpenMode(Disk());
        model.FileName = "Notes";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Notes.ream", result.Path);
    }

    [Fact]
    public void Open_rejects_a_file_that_is_not_a_ream()
    {
        var model = OpenMode(Disk());

        model.FileName = "readme.txt";
        var result = model.TryAccept();
        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Null(result.Path);
        Assert.Equal("\"readme.txt\" isn't a ream file (.ream).", result.Error);

        model.FileName = "LICENSE";
        Assert.Equal(AcceptOutcome.Rejected, model.TryAccept().Outcome);
    }

    [Theory]
    [InlineData("Missing.ream")]
    [InlineData("Missing")]
    [InlineData(@"C:\Nowhere\Ghost.ream")]
    [InlineData("bad|name")]
    public void Open_rejects_a_ream_that_does_not_exist(string name)
    {
        var model = OpenMode(Disk());
        model.FileName = name;

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Contains("doesn't exist", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void Open_rejects_an_empty_name(string name)
    {
        var model = OpenMode(Disk());
        model.FileName = name;

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Equal("Choose a ream to open.", result.Error);
    }

    [Fact]
    public void Open_with_a_folder_in_the_name_box_goes_there_instead_and_clears_the_box()
    {
        var model = OpenMode(Disk());
        model.FileName = "Sub";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.NavigatedInstead, result.Outcome);
        Assert.Equal(Docs + @"\Sub", result.Path);
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
        Assert.Equal("", model.FileName);
        Assert.True(model.CanGoBack);
    }

    [Fact]
    public void Open_with_a_full_folder_path_goes_there_instead()
    {
        var model = OpenMode(Disk());
        model.FileName = Desktop;

        Assert.Equal(AcceptOutcome.NavigatedInstead, model.TryAccept().Outcome);
        Assert.Equal(Desktop, model.CurrentDirectory);
    }

    [Fact]
    public void Open_uses_the_name_box_not_the_selection()
    {
        var model = OpenMode(Disk());
        model.Selected = model.Entries.First(e => e.Name == "Notes.ream");
        model.FileName = "Work.ream";

        Assert.Equal(Docs + @"\Work.ream", model.TryAccept().Path);
    }

    [Fact]
    public void Open_accepts_what_a_selected_file_put_in_the_name_box()
    {
        var model = OpenMode(Disk());
        model.Selected = model.Entries.First(e => e.Name == "Notes.ream");

        Assert.Equal(Docs + @"\Notes.ream", model.TryAccept().Path);
    }

    // ----- Accept: Save (New ream, Save As) -----

    [Fact]
    public void Save_accepts_a_valid_name_and_adds_the_extension()
    {
        var model = SaveMode(Disk());
        model.FileName = "Fresh";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Fresh.ream", result.Path);
    }

    [Theory]
    [InlineData("Fresh.ream")]
    [InlineData("Fresh.REAM")]
    [InlineData("  Fresh  ")]
    [InlineData("\"Fresh\"")]
    public void Save_keeps_one_ream_extension_and_ignores_case_quotes_and_spaces(string typed)
    {
        var model = SaveMode(Disk());
        model.FileName = typed;

        Assert.Equal(Docs + @"\Fresh.ream", model.TryAccept().Path);
    }

    [Fact]
    public void Save_keeps_another_typed_extension_as_part_of_the_name()
    {
        var model = SaveMode(Disk());
        model.FileName = "Plans.txt";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Plans.txt.ream", result.Path);
    }

    [Fact]
    public void Save_accepts_the_suggested_name_when_it_is_free()
    {
        var model = SaveMode(Disk(), suggested: "Untitled");

        Assert.Equal(Docs + @"\Untitled.ream", model.TryAccept().Path);
    }

    [Fact]
    public void Save_accepts_a_full_path_and_a_relative_path_to_an_existing_folder()
    {
        var model = SaveMode(Disk());

        model.FileName = Desktop + @"\Elsewhere";
        Assert.Equal(Desktop + @"\Elsewhere.ream", model.TryAccept().Path);

        model.FileName = @"Sub\Deeper.ream";
        Assert.Equal(Docs + @"\Sub\Deeper.ream", model.TryAccept().Path);

        model.FileName = @"..\Desktop\Up";
        Assert.Equal(Desktop + @"\Up.ream", model.TryAccept().Path);
    }

    [Fact]
    public void Save_expands_environment_variables()
    {
        const string variable = "REAM_FB_TEST_SAVE";
        Environment.SetEnvironmentVariable(variable, "FromEnv");
        try
        {
            var model = SaveMode(Disk());
            model.FileName = $"%{variable}%";

            Assert.Equal(Docs + @"\FromEnv.ream", model.TryAccept().Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("Notes")]
    [InlineData("Notes.ream")]
    [InlineData("notes.REAM")]
    [InlineData("NOTES")]
    public void Save_rejects_a_name_taken_by_an_existing_ream(string typed)
    {
        var model = SaveMode(Disk());
        model.FileName = typed;

        var result = model.TryAccept();

        // "Notes" alone is also the data folder, so it may navigate instead; either way it is never accepted.
        Assert.NotEqual(AcceptOutcome.Accepted, result.Outcome);
        if (result.Outcome == AcceptOutcome.Rejected)
            Assert.Equal("There is already a ream called \"" + typed.Replace(".ream", "", StringComparison.OrdinalIgnoreCase) + "\" here. Choose another name.", result.Error);
    }

    [Fact]
    public void Save_rejects_a_name_taken_by_a_ream_file_with_the_exact_message()
    {
        var fs = Disk();
        fs.AddFile(Docs + @"\Lonely.ream");
        var model = SaveMode(fs);
        model.FileName = "Lonely";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Equal("There is already a ream called \"Lonely\" here. Choose another name.", result.Error);
        Assert.Null(result.Path);
    }

    [Fact]
    public void Save_rejects_a_name_whose_data_folder_exists_even_without_the_ream_file()
    {
        var fs = Disk();
        fs.AddDirectory(Docs + @"\Ghost");
        var model = SaveMode(fs);
        model.FileName = "Ghost.ream";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Equal("There is already a ream called \"Ghost\" here. Choose another name.", result.Error);
    }

    [Fact]
    public void Save_with_an_existing_folder_in_the_name_box_goes_there_instead_and_clears_the_box()
    {
        var model = SaveMode(Disk(), suggested: "Sub");

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.NavigatedInstead, result.Outcome);
        Assert.Equal(Docs + @"\Sub", result.Path);
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
        Assert.Equal("", model.FileName);
    }

    [Fact]
    public void Save_with_a_full_folder_path_or_dot_dot_goes_there_instead()
    {
        var model = SaveMode(Disk());

        model.FileName = Desktop;
        Assert.Equal(AcceptOutcome.NavigatedInstead, model.TryAccept().Outcome);
        Assert.Equal(Desktop, model.CurrentDirectory);

        model.FileName = "..";
        Assert.Equal(AcceptOutcome.NavigatedInstead, model.TryAccept().Outcome);
        Assert.Equal(@"C:\Users\Joe", model.CurrentDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData(".ream")]
    public void Save_rejects_an_empty_name(string typed)
    {
        var model = SaveMode(Disk());
        model.FileName = typed;

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Equal("Type a name for the ream.", result.Error);
    }

    [Theory]
    [InlineData("a*b")]
    [InlineData("what?")]
    [InlineData("a:b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("Foo.")]
    [InlineData("Foo .ream")]
    public void Save_rejects_names_with_invalid_characters_or_endings(string typed)
    {
        var model = SaveMode(Disk());
        model.FileName = typed;

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Aux.ream")]
    [InlineData("com3")]
    [InlineData("lpt1.backup")]
    public void Save_rejects_reserved_names(string typed)
    {
        var model = SaveMode(Disk());
        model.FileName = typed;

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Contains("reserves", result.Error);
    }

    [Fact]
    public void Save_rejects_a_name_that_is_too_long_and_accepts_one_at_the_limit()
    {
        var model = SaveMode(Disk());

        model.FileName = new string('x', 101);
        var tooLong = model.TryAccept();
        Assert.Equal(AcceptOutcome.Rejected, tooLong.Outcome);
        Assert.Contains("shorter", tooLong.Error);

        model.FileName = new string('x', 100);
        Assert.Equal(AcceptOutcome.Accepted, model.TryAccept().Outcome);
    }

    [Fact]
    public void Save_rejects_when_the_current_folder_is_gone()
    {
        var fs = Disk();
        var model = SaveMode(fs);
        fs.Remove(Docs);
        model.FileName = "Fresh";

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Contains("doesn't exist", result.Error);
    }

    [Theory]
    [InlineData(@"C:\Nowhere\Fresh")]
    [InlineData(@"Missing\Fresh")]
    [InlineData(@"Sub\Nope\")]
    public void Save_rejects_a_name_in_a_folder_that_does_not_exist(string typed)
    {
        var model = SaveMode(Disk());
        model.FileName = typed;

        var result = model.TryAccept();

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Contains("doesn't exist", result.Error);
    }

    [Fact]
    public void Save_mode_never_accepts_a_name_that_is_occupied_however_it_is_written()
    {
        var fs = Disk();
        fs.AddFile(Docs + @"\Lonely.ream").AddDirectory(Docs + @"\OnlyData").AddFile(Desktop + @"\Far.ream");
        var model = SaveMode(fs, Desktop);

        string[] occupied =
        [
            Docs + @"\Notes.ream", Docs + @"\Notes", Docs + @"\notes.REAM", Docs + @"\Work.ream",
            Docs + @"\Lonely", Docs + @"\Lonely.ream", Docs + @"\OnlyData.ream", Docs + @"\ONLYDATA.ream",
            $"\"{Docs}\\Notes.ream\"", @"..\Documents\Notes.ream", @"..\Documents\Lonely",
            "Far", "Far.ream", "FAR.REAM", "\"far\"", "  Far.ream  ", Desktop + @"\Far",
        ];

        foreach (string typed in occupied)
        {
            model.Navigate(Desktop);
            model.FileName = typed;

            var result = model.TryAccept();

            Assert.False(result.IsAccepted, $"\"{typed}\" is taken but was accepted as {result.Path}");
            Assert.NotEqual(AcceptOutcome.Accepted, result.Outcome);
        }
    }

    [Fact]
    public void Save_mode_accepts_the_free_names_next_to_the_occupied_ones()
    {
        var model = SaveMode(Disk());

        foreach (string typed in new[] { "Notes 2", "Work2.ream", "Sub2", "readme", "LICENSE2" })
        {
            model.FileName = typed;
            Assert.Equal(AcceptOutcome.Accepted, model.TryAccept().Outcome);
        }
    }

    [Fact]
    public void An_accepted_save_path_never_ends_anywhere_but_ream()
    {
        var model = SaveMode(Disk());

        foreach (string typed in new[] { "a", "a.ream", "a.b", "a.b.ream", "A.REAM", "a b" })
        {
            model.FileName = typed;
            string path = model.TryAccept().Path!;
            Assert.EndsWith(".ream", path);
            Assert.False(path.EndsWith(".ream.ream", StringComparison.Ordinal));
        }
    }

    // ----- Double click / Enter on an item -----

    [Fact]
    public void OpenEntry_on_a_folder_enters_it()
    {
        var model = OpenMode(Disk());

        var result = model.OpenEntry(model.Entries.First(e => e.Name == "Sub"));

        Assert.Equal(AcceptOutcome.NavigatedInstead, result.Outcome);
        Assert.Equal(Docs + @"\Sub", result.Path);
        Assert.Equal(Docs + @"\Sub", model.CurrentDirectory);
    }

    [Fact]
    public void OpenEntry_on_a_ream_in_open_mode_accepts_it()
    {
        var model = OpenMode(Disk());

        var result = model.OpenEntry(model.Entries.First(e => e.Name == "Work.ream"));

        Assert.Equal(AcceptOutcome.Accepted, result.Outcome);
        Assert.Equal(Docs + @"\Work.ream", result.Path);
        Assert.Equal("Work.ream", model.FileName);
    }

    [Fact]
    public void OpenEntry_on_a_file_that_is_not_a_ream_is_rejected()
    {
        var model = OpenMode(Disk());
        model.ShowAllFiles = true;

        var result = model.OpenEntry(model.Entries.First(e => e.Name == "readme.txt"));

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Contains("isn't a ream file", result.Error);
    }

    [Fact]
    public void OpenEntry_on_an_existing_ream_in_save_mode_fills_the_name_but_is_never_accepted()
    {
        var model = SaveMode(Disk());

        var result = model.OpenEntry(model.Entries.First(e => e.Name == "Notes.ream"));

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Contains("There is already a ream called \"Notes\"", result.Error);
        Assert.Equal("Notes.ream", model.FileName);
    }

    [Fact]
    public void OpenEntry_on_a_folder_that_has_vanished_is_rejected()
    {
        var fs = Disk();
        var model = OpenMode(fs);
        var sub = model.Entries.First(e => e.Name == "Sub");
        fs.Remove(sub.FullPath);

        var result = model.OpenEntry(sub);

        Assert.Equal(AcceptOutcome.Rejected, result.Outcome);
        Assert.Equal(Docs, model.CurrentDirectory);
    }

    // ----- Notifications -----

    [Fact]
    public void Navigating_raises_the_directory_listing_and_history_notifications()
    {
        var model = OpenMode(Disk());
        var raised = Watch(model);

        model.Navigate(Desktop);

        Assert.Contains(nameof(FileBrowserModel.CurrentDirectory), raised);
        Assert.Contains(nameof(FileBrowserModel.Breadcrumbs), raised);
        Assert.Contains(nameof(FileBrowserModel.Entries), raised);
        Assert.Contains(nameof(FileBrowserModel.CanGoBack), raised);
        Assert.DoesNotContain(nameof(FileBrowserModel.CanGoForward), raised);
        Assert.DoesNotContain(nameof(FileBrowserModel.CanGoUp), raised);
    }

    [Fact]
    public void Back_and_Forward_raise_the_history_flags()
    {
        var model = OpenMode(Disk());
        model.Navigate(Desktop);
        var raised = Watch(model);

        model.Back();
        Assert.Contains(nameof(FileBrowserModel.CanGoBack), raised);
        Assert.Contains(nameof(FileBrowserModel.CanGoForward), raised);

        raised.Clear();
        model.Forward();
        Assert.Contains(nameof(FileBrowserModel.CanGoBack), raised);
        Assert.Contains(nameof(FileBrowserModel.CanGoForward), raised);
    }

    [Fact]
    public void Reaching_a_drive_root_raises_CanGoUp()
    {
        var model = OpenMode(Disk(), @"C:\Users");
        var raised = Watch(model);

        model.Up();

        Assert.Contains(nameof(FileBrowserModel.CanGoUp), raised);
    }

    [Fact]
    public void A_listing_error_raises_ListingError_going_in_and_coming_out()
    {
        var fs = Disk();
        fs.Deny(Docs + @"\Sub");
        var model = OpenMode(fs);
        var raised = Watch(model);

        model.Navigate(Docs + @"\Sub");
        Assert.Contains(nameof(FileBrowserModel.ListingError), raised);

        raised.Clear();
        model.Up();
        Assert.Contains(nameof(FileBrowserModel.ListingError), raised);
    }

    [Fact]
    public void Changing_the_name_and_the_selection_raise_their_notifications()
    {
        var model = OpenMode(Disk());
        var raised = Watch(model);

        model.FileName = "abc";
        Assert.Equal(new[] { nameof(FileBrowserModel.FileName) }, raised);

        raised.Clear();
        model.Selected = model.Entries.First(e => e.Name == "Notes.ream");
        Assert.Contains(nameof(FileBrowserModel.Selected), raised);
        Assert.Contains(nameof(FileBrowserModel.FileName), raised);
    }

    [Fact]
    public void Filter_and_sort_changes_raise_Entries_but_not_the_directory()
    {
        var model = OpenMode(Disk());
        var raised = Watch(model);

        model.ShowAllFiles = true;
        model.SortBy = SortColumn.Modified;
        model.SortDescending = true;

        Assert.Equal(3, raised.Count(p => p == nameof(FileBrowserModel.Entries)));
        Assert.DoesNotContain(nameof(FileBrowserModel.CurrentDirectory), raised);
        Assert.DoesNotContain(nameof(FileBrowserModel.Breadcrumbs), raised);
    }

    [Fact]
    public void Setting_a_value_it_already_has_raises_nothing()
    {
        var model = SaveMode(Disk(), suggested: "x");
        var raised = Watch(model);

        model.FileName = "x";
        model.ShowAllFiles = false;
        model.SortBy = SortColumn.Name;
        model.SortDescending = false;
        model.Selected = null;
        model.Back();
        model.Forward();

        Assert.Empty(raised);
    }

    [Fact]
    public void CreateFolder_raises_Entries_and_Selected()
    {
        var model = OpenMode(Disk());
        var raised = Watch(model);

        model.CreateFolder("Fresh", out _);

        Assert.Contains(nameof(FileBrowserModel.Entries), raised);
        Assert.Contains(nameof(FileBrowserModel.Selected), raised);
    }

    [Fact]
    public void NavigateToText_of_a_file_raises_Selected_and_FileName()
    {
        var model = OpenMode(Disk(), Desktop);
        var raised = Watch(model);

        model.NavigateToText(Docs + @"\Notes.ream");

        Assert.Contains(nameof(FileBrowserModel.CurrentDirectory), raised);
        Assert.Contains(nameof(FileBrowserModel.Selected), raised);
        Assert.Contains(nameof(FileBrowserModel.FileName), raised);
    }

    // ----- Big folders -----

    [Fact]
    public void A_folder_with_thousands_of_entries_lists_and_sorts_quickly()
    {
        var fs = new FakeFileSystem();
        var modified = new DateTime(2026, 1, 1);
        for (int i = 0; i < 4000; i++)
        {
            fs.AddFile($@"C:\Big\note {i}.ream", modified: modified.AddMinutes(i));
            if (i % 4 == 0) fs.AddFile($@"C:\Big\other {i}.txt");
            if (i % 10 == 0) fs.AddDirectory($@"C:\Big\dir {i}");
        }

        var watch = Stopwatch.StartNew();
        var model = OpenMode(fs, @"C:\Big");
        model.ShowAllFiles = true;
        model.SortBy = SortColumn.Modified;
        model.SortDescending = true;
        model.SortBy = SortColumn.Name;
        model.SortDescending = false;
        watch.Stop();

        Assert.Equal(400 + 4000 + 1000, model.Entries.Count);
        Assert.True(model.Entries.Take(400).All(e => e.IsDirectory));
        Assert.Equal("dir 0", model.Entries[0].Name);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }
}
