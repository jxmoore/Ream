using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

using Ream.App.Editing;
using Ream.App.Views;

namespace Ream.Tests;

public class DocumentSearchTests
{
    // A RichTextBox document always ends in a trailing paragraph break; trim it so tests can compare plain text.
    private static string TextOf(FlowDocument document) => new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd();

    private static string TextBefore(RichTextBox editor, TextPointer p) => new TextRange(editor.Document.ContentStart, p).Text;

    private const string Doc = """<ReamNote schemaVersion="1"><Doc><P><R>alpha beta alpha</R></P></Doc></ReamNote>""";

    [Fact]
    public void FindNext_FindsTheFirstMatch_FromTheGivenPosition() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Doc);

        var match = DocumentSearch.FindNext(fx.Editor, "alpha", matchCase: false, fx.Editor.Document.ContentStart);

        Assert.NotNull(match);
        Assert.Equal(0, TextBefore(fx.Editor, match.Value.Start).Length);
        Assert.Equal("alpha", new TextRange(match.Value.Start, match.Value.End).Text);
    });

    [Fact]
    public void FindNext_IsCaseInsensitive_ByDefault() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>Alpha beta</R></P></Doc></ReamNote>""");

        var match = DocumentSearch.FindNext(fx.Editor, "ALPHA", matchCase: false, fx.Editor.Document.ContentStart);

        Assert.NotNull(match);
        Assert.Equal("Alpha", new TextRange(match.Value.Start, match.Value.End).Text);
    });

    [Fact]
    public void FindNext_IsCaseSensitive_WhenAsked() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>Alpha alpha</R></P></Doc></ReamNote>""");

        var match = DocumentSearch.FindNext(fx.Editor, "alpha", matchCase: true, fx.Editor.Document.ContentStart);

        Assert.NotNull(match);
        Assert.Equal(6, TextBefore(fx.Editor, match.Value.Start).Length); // "Alpha " - the capitalized one doesn't count
    });

    [Fact]
    public void FindNext_WrapsAroundToTheStart_WhenNothingIsLeftAfterTheGivenPosition() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Doc);

        var match = DocumentSearch.FindNext(fx.Editor, "alpha", matchCase: false, fx.Editor.Document.ContentEnd);

        Assert.NotNull(match);
        Assert.Equal(0, TextBefore(fx.Editor, match.Value.Start).Length);
    });

    [Fact]
    public void FindNext_ReturnsNull_WhenTheQueryDoesNotOccur() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Doc);

        Assert.Null(DocumentSearch.FindNext(fx.Editor, "gamma", matchCase: false, fx.Editor.Document.ContentStart));
    });

    [Fact]
    public void FindNext_ReturnsNull_ForAnEmptyQuery() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Doc);

        Assert.Null(DocumentSearch.FindNext(fx.Editor, "", matchCase: false, fx.Editor.Document.ContentStart));
    });

    [Fact]
    public void ReplaceAll_ReplacesEveryOccurrence_AndReturnsTheCount() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>cat cat dog cat</R></P></Doc></ReamNote>""");

        int count = DocumentSearch.ReplaceAll(fx.Editor, "cat", "dog", matchCase: false);

        Assert.Equal(3, count);
        Assert.Equal("dog dog dog dog", TextOf(fx.Editor.Document));
    });

    [Fact]
    public void ReplaceAll_WithTheQueryInsideTheReplacement_StillTerminates() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>a a</R></P></Doc></ReamNote>""");

        int count = DocumentSearch.ReplaceAll(fx.Editor, "a", "aa", matchCase: false);

        Assert.Equal(2, count);
        Assert.Equal("aa aa", TextOf(fx.Editor.Document));
    });
}

public class FindReplaceWindowTests
{
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static FindReplaceWindow ShowFind(RichTextBox editor, bool withReplace = false)
    {
        var window = new FindReplaceWindow(editor, withReplace)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        Ui.Settle();
        return window;
    }

    private static T Part<T>(Window window, string name) where T : class => (T)window.FindName(name);

    [Fact]
    public void FindMode_HidesTheReplaceControls() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>hello world</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor);
        try
        {
            Assert.Equal("Find", window.Title);
            Assert.Equal(Visibility.Collapsed, Part<UIElement>(window, "ReplaceBox").Visibility);
            Assert.Equal(Visibility.Collapsed, Part<UIElement>(window, "ReplaceButton").Visibility);
            Assert.Equal(Visibility.Collapsed, Part<UIElement>(window, "ReplaceAllButton").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ReplaceMode_ShowsTheReplaceControls() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>hello world</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor, withReplace: true);
        try
        {
            Assert.Equal("Replace", window.Title);
            Assert.Equal(Visibility.Visible, Part<UIElement>(window, "ReplaceBox").Visibility);
            Assert.Equal(Visibility.Visible, Part<UIElement>(window, "ReplaceButton").Visibility);
            Assert.Equal(Visibility.Visible, Part<UIElement>(window, "ReplaceAllButton").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SetMode_SwitchesAnAlreadyOpenWindow() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>hello world</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor, withReplace: false);
        try
        {
            window.SetMode(true);
            Assert.Equal("Replace", window.Title);
            Assert.Equal(Visibility.Visible, Part<UIElement>(window, "ReplaceBox").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void FindNext_SelectsTheMatch_InTheEditor() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>hello world</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor);
        try
        {
            Part<TextBox>(window, "FindBox").Text = "world";
            Click(Part<Button>(window, "FindNextButton"));

            Assert.Equal("world", fx.Editor.Selection.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void NoMatch_ShowsPhraseNotFound() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>hello</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor);
        try
        {
            Part<TextBox>(window, "FindBox").Text = "zzz";
            Click(Part<Button>(window, "FindNextButton"));

            Assert.Equal("Phrase not found.", Part<TextBlock>(window, "StatusText").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Replace_OnlyReplacesTheCurrentSelection_WhenItMatches() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>cat cat</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor, withReplace: true);
        try
        {
            Part<TextBox>(window, "FindBox").Text = "cat";
            Part<TextBox>(window, "ReplaceBox").Text = "dog";

            Click(Part<Button>(window, "ReplaceButton")); // finds (nothing was selected yet), doesn't replace
            Assert.Equal("cat cat", new TextRange(fx.Editor.Document.ContentStart, fx.Editor.Document.ContentEnd).Text.TrimEnd());

            Click(Part<Button>(window, "ReplaceButton")); // now the first "cat" is selected and matches: replaced, then finds the next
            Assert.Equal("dog cat", new TextRange(fx.Editor.Document.ContentStart, fx.Editor.Document.ContentEnd).Text.TrimEnd());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ReplaceAll_UpdatesTheEditor_AndReportsTheCount() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>cat cat</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor, withReplace: true);
        try
        {
            Part<TextBox>(window, "FindBox").Text = "cat";
            Part<TextBox>(window, "ReplaceBox").Text = "dog";
            Click(Part<Button>(window, "ReplaceAllButton"));

            Assert.Equal("dog dog", new TextRange(fx.Editor.Document.ContentStart, fx.Editor.Document.ContentEnd).Text.TrimEnd());
            Assert.Equal("2 replacements made.", Part<TextBlock>(window, "StatusText").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void CloseButton_ClosesTheWindow() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>hello</R></P></Doc></ReamNote>""");
        var window = ShowFind(fx.Editor);
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Click(Part<Button>(window, "CloseButton"));

        Assert.True(closed);
    });
}

/// <summary>Find/Replace end to end: the keybindings and ribbon buttons ultimately reach a FindReplaceWindow through MainWindow.</summary>
public class FindReplaceIntegrationTests
{
    private static IEnumerable<FindReplaceWindow> OpenFindWindows() =>
        System.Windows.Application.Current.Windows.OfType<FindReplaceWindow>();

    [Fact]
    public void FindCommand_OpensAFindWindow_OnTheFocusedEditor() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        fx.App.FindCommand.Execute(null);
        Ui.Settle();

        var window = Assert.Single(OpenFindWindows());
        try { Assert.Equal("Find", window.Title); }
        finally { window.Close(); }
    });

    [Fact]
    public void ReplaceCommand_ReusesAnOpenFindWindow_SwitchedToReplaceMode() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        fx.App.FindCommand.Execute(null);
        Ui.Settle();
        var first = Assert.Single(OpenFindWindows());

        fx.App.ReplaceCommand.Execute(null);
        Ui.Settle();

        try
        {
            var windows = OpenFindWindows().ToList();
            Assert.Same(first, Assert.Single(windows));
            Assert.Equal("Replace", windows[0].Title);
        }
        finally { first.Close(); }
    });
}
