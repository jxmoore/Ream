using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Models;
using Ream.Persistence.NoteFormat;
using Ream.Persistence.Storage;

namespace Ream.Tests;

/// <summary>A real note view and toolbar hosted in an off-screen window, backed by a real repository in a temp folder.</summary>
internal sealed class EditorFixture : IDisposable
{
    public EditorFixture(string body = "")
    {
        Dir = new TempDir();
        Repo = TestReam.Repo(Dir.Combine("Docs"));
        Workspace = new WorkspaceViewModel("W", Repo);
        Note = new NoteViewModel { Title = "Untitled", Body = body };
        Workspace.LoadNotes([Note], Note.Id);

        View = new NoteColumnView { DataContext = Note };
        Toolbar = new RibbonView();
        var layout = new DockPanel();
        DockPanel.SetDock(Toolbar, Dock.Top);
        layout.Children.Add(Toolbar);
        layout.Children.Add(View);

        Window = Ui.Show(layout);
        Toolbar.AttachEditor(Editor);
    }

    public TempDir Dir { get; }
    public DocumentRepository Repo { get; }
    public WorkspaceViewModel Workspace { get; }
    public NoteViewModel Note { get; }
    public NoteColumnView View { get; }
    public RibbonView Toolbar { get; }
    public Window Window { get; }
    public RichTextBox Editor => View.Editor;

    /// <summary>The saved form of the note after pending edits are written into it.</summary>
    public string Saved()
    {
        Note.FlushDocument();
        return Note.Body;
    }

    public void Dispose()
    {
        Window.Close();
        Dir.Dispose();
    }
}

public class EditorIntegrationTests
{
    private const string Plain = """<ReamNote schemaVersion="1"><Doc><P><R>hello world</R></P></Doc></ReamNote>""";

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static string TextOf(FlowDocument document) => new TextRange(document.ContentStart, document.ContentEnd).Text;

    private static List<Run> RunsOf(FlowDocument document)
    {
        var runs = new List<Run>();
        for (var p = document.ContentStart; p is not null && p.CompareTo(document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetAdjacentElement(LogicalDirection.Forward) is Run run && !runs.Contains(run)) runs.Add(run);
        return runs;
    }

    private static List<Image> ImagesOf(FlowDocument document)
    {
        var images = new List<Image>();
        for (var p = document.ContentStart; p is not null && p.CompareTo(document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            // A container is reported at both its start and its end, so the same image would count twice.
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: Image image } && !images.Contains(image)) images.Add(image);
        return images;
    }

    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 90; pixels[i + 2] = 40; pixels[i + 3] = 255; }
        return NoteImage.EncodePng(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4));
    }

    // ----- Loading and typing -----

    [Fact]
    public void SavedFormatting_IsShown_InTheThemeColors() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>plain </R><R b="1">bold</R></P></Doc></ReamNote>""");

        var runs = RunsOf(fx.Editor.Document);

        Assert.Equal(FontWeights.Bold, runs.Single(r => r.Text == "bold").FontWeight);
        var themeText = ((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color;
        Assert.Equal(themeText, ((SolidColorBrush)runs[0].Foreground).Color);
        Assert.Equal(14, runs[0].FontSize);
    });

    [Fact]
    public void OpeningANote_DoesNotCountAsAnEdit() => Ui.Run(() =>
    {
        const string body = """<ReamNote schemaVersion="1"><Doc><P><R b="1">exactly as saved</R></P></Doc></ReamNote>""";
        using var fx = new EditorFixture(body);

        Assert.Equal(body, fx.Saved());
    });

    [Fact]
    public void OlderPlainTextNotes_Open_AsParagraphs() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("first line\nsecond line");

        Assert.Equal(2, fx.Editor.Document.Blocks.Count);
        Assert.Contains("second line", TextOf(fx.Editor.Document));
    });

    [Fact]
    public void Typing_IsSaved_AndRetitlesTheNote() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);

        fx.Editor.AppendText(" typed");

        string saved = fx.Saved();
        Assert.Contains("hello world typed", NoteContent.ToPlainText(saved));
        Assert.Equal("hello world typed", fx.Note.Title);
    });

    [Fact]
    public void EmptyNote_KeepsItsPlaceholderTitle() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("");
        fx.Note.Title = "Untitled 7";

        fx.Editor.AppendText("   ");
        fx.Note.FlushDocument();

        Assert.Equal("Untitled 7", fx.Note.Title);
    });

    [Fact]
    public void EditsInAReplacedView_AreNotLost() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.AppendText(" more");

        var replacement = new NoteColumnView { DataContext = fx.Note };
        replacement.EnsureLoaded();

        Assert.Contains("more", TextOf(replacement.Editor.Document));
    });

    // ----- Toolbar -----

    [Fact]
    public void Bold_ShowsInTheButton_AndIsSaved() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.BoldButton);

        Assert.All(RunsOf(fx.Editor.Document), r => Assert.Equal(FontWeights.Bold, r.FontWeight));
        Assert.True(fx.Toolbar.BoldButton.IsChecked);
        Assert.Contains("b=\"1\"", fx.Saved());
    });

    [Fact]
    public void BoldButton_ClearsAgain_WhenClickedTwice() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.BoldButton);
        Click(fx.Toolbar.BoldButton);

        Assert.All(RunsOf(fx.Editor.Document), r => Assert.Equal(FontWeights.Normal, r.FontWeight));
        Assert.False(fx.Toolbar.BoldButton.IsChecked);
    });

    [Fact]
    public void ItalicUnderlineAndStrikethrough_AreSaved() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.ItalicButton);
        Click(fx.Toolbar.UnderlineButton);
        Click(fx.Toolbar.StrikeButton);

        string saved = fx.Saved();
        Assert.Contains("i=\"1\"", saved);
        Assert.Contains("u=\"1\"", saved);
        Assert.Contains("s=\"1\"", saved);
        Assert.True(fx.Toolbar.ItalicButton.IsChecked);
        Assert.True(fx.Toolbar.UnderlineButton.IsChecked);
        Assert.True(fx.Toolbar.StrikeButton.IsChecked);
    });

    [Fact]
    public void Strikethrough_CanBeToggledOffWithoutRemovingUnderline() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        Click(fx.Toolbar.UnderlineButton);
        Click(fx.Toolbar.StrikeButton);

        Click(fx.Toolbar.StrikeButton);

        string saved = fx.Saved();
        Assert.Contains("u=\"1\"", saved);
        Assert.Contains("s=\"0\"", saved);
    });

    [Fact]
    public void BulletsAndNumbering_MakeLists() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.BulletsButton);
        Assert.Contains("<UL>", fx.Saved());
        Assert.True(fx.Toolbar.BulletsButton.IsChecked);

        Click(fx.Toolbar.NumbersButton);
        Assert.Contains("<OL>", fx.Saved());
        Assert.True(fx.Toolbar.NumbersButton.IsChecked);
    });

    [Fact]
    public void Alignment_IsSaved() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.AlignCenterButton);

        Assert.Contains("align=\"center\"", fx.Saved());
        Assert.True(fx.Toolbar.AlignCenterButton.IsChecked);
    });

    [Fact]
    public void HeadingStyle_SetsSizeAndWeight_AndNormalRemovesThem() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.StyleHeading1Button);
        string heading = fx.Saved();
        Assert.Contains("size=\"28\"", heading);
        Assert.Contains("b=\"1\"", heading);

        Click(fx.Toolbar.StyleNormalButton);
        string normal = fx.Saved();
        Assert.DoesNotContain("size=", normal);
        Assert.DoesNotContain("b=\"1\"", normal);
    });

    [Fact]
    public void FontAndSizeDropdowns_ApplyToTheSelection() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.FontBox.SelectedItem = "Consolas";
        fx.Toolbar.SizeBox.SelectedItem = 24d;

        string saved = fx.Saved();
        Assert.Contains("font=\"Consolas\"", saved);
        Assert.Contains("size=\"24\"", saved);
    });

    [Fact]
    public void TextColor_IsSaved_AndAutomaticRemovesIt() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyTextColor(Colors.Red);
        Assert.Contains("color=\"#FF0000\"", fx.Saved());

        fx.Toolbar.ApplyTextColor(null);
        Assert.DoesNotContain("color=", fx.Saved());
    });

    [Fact]
    public void Highlight_IsSaved_AndNoneRemovesIt() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyHighlight(Color.FromRgb(255, 243, 163));
        Assert.Contains("bg=\"#FFF3A3\"", fx.Saved());

        fx.Toolbar.ApplyHighlight(null);
        Assert.DoesNotContain("bg=", fx.Saved());
    });

    [Fact]
    public void SubscriptAndSuperscript_ToggleAndAreSaved() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        Click(fx.Toolbar.SubscriptButton);
        Assert.Contains("va=\"sub\"", fx.Saved());
        Assert.True(fx.Toolbar.SubscriptButton.IsChecked);

        // Toggling it back off removes the marker, and turns superscript on instead is a separate click.
        Click(fx.Toolbar.SubscriptButton);
        Assert.DoesNotContain("va=", fx.Saved());

        Click(fx.Toolbar.SuperscriptButton);
        Assert.Contains("va=\"super\"", fx.Saved());
    });

    [Fact]
    public void ClearFormatting_RemovesCharacterFormattingFromTheSelection() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        Click(fx.Toolbar.BoldButton);
        Click(fx.Toolbar.ItalicButton);
        fx.Toolbar.ApplyTextColor(Colors.Red);
        Assert.Contains("b=\"1\"", fx.Saved());

        fx.Editor.SelectAll();
        Click(fx.Toolbar.ClearFormattingButton);

        string cleared = fx.Saved();
        Assert.DoesNotContain("b=\"1\"", cleared);
        Assert.DoesNotContain("i=\"1\"", cleared);
        Assert.DoesNotContain("color=", cleared);
    });

    [Fact]
    public void ChangeCase_UppercasesTheSelection() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.TransformCase(t => t.ToUpperInvariant());

        Assert.Equal("HELLO WORLD", TextOf(fx.Editor.Document).TrimEnd());
    });

    [Fact]
    public void LineSpacing_SetsLineHeight_AsAMultipleOfTheFontSize() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyLineSpacing(2.0);

        Assert.Contains("lh=", fx.Saved());
    });

    [Fact]
    public void Shading_SetsAndClearsTheParagraphBackground() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyShading(Colors.LightBlue);
        Assert.Contains("bg=", fx.Saved());

        fx.Toolbar.ApplyShading(null);
        Assert.DoesNotContain("bg=", fx.Saved());
    });

    [Fact]
    public void Borders_AddAndRemoveAParagraphBorder() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyBorder((_, _) => new Thickness(1));
        Assert.Contains("bd=", fx.Saved());

        fx.Toolbar.ApplyBorder((_, _) => new Thickness(0));
        Assert.DoesNotContain("bd=", fx.Saved());
    });

    [Fact]
    public void Sort_OrdersSelectedParagraphsAlphabetically() => Ui.Run(() =>
    {
        using var fx = new EditorFixture("""<ReamNote schemaVersion="1"><Doc><P><R>banana</R></P><P><R>apple</R></P><P><R>cherry</R></P></Doc></ReamNote>""");
        fx.Editor.SelectAll();

        fx.Toolbar.SortParagraphs(ascending: true);

        Assert.Equal("apple\r\nbanana\r\ncherry", TextOf(fx.Editor.Document).TrimEnd());
    });

    [Fact]
    public void MoreStyles_SetSizeAndSlant_NotJustWeight() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.Toolbar.ApplyStyle(6); // Subtitle: italic, not bold
        string saved = fx.Saved();
        Assert.Contains("i=\"1\"", saved);
        Assert.DoesNotContain("b=\"1\"", saved);
    });

    [Fact]
    public void TheZoomResource_ScalesTheEditorsLayoutTransform() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        var theme = new ThemeService(Application.Current);
        try
        {
            var transform = Assert.IsType<ScaleTransform>(fx.Editor.LayoutTransform);
            Assert.Equal(1.0, transform.ScaleX);

            theme.Apply("dark", zoomPercent: 150);
            Ui.Settle();

            Assert.Equal(1.5, transform.ScaleX);
            Assert.Equal(1.5, transform.ScaleY);
        }
        finally
        {
            theme.Apply("dark"); // back to 100%, for whichever test shares the Application next
        }
    });

    [Fact]
    public void Undo_RevertsToolbarFormatting() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        Click(fx.Toolbar.BoldButton);

        ApplicationCommands.Undo.Execute(null, fx.Editor);

        Assert.DoesNotContain("b=\"1\"", fx.Saved());
    });

    // ----- Images -----

    [Fact]
    public void InsertedImage_IsSavedNextToTheNote_AndTagged() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);

        fx.View.InsertImage(Png(64, 32));
        Ui.Settle();

        var image = Assert.Single(ImagesOf(fx.Editor.Document));
        string name = NoteImage.GetAssetName(image)!;
        Assert.Equal(64, image.Width);
        Assert.Equal(32, image.Height);
        Assert.NotNull(fx.Repo.GetAssetPath(fx.Workspace.FolderName, fx.Note.Id, name));

        string saved = fx.Saved();
        Assert.Contains("asset://" + name, saved);
        Assert.DoesNotContain("base64", saved);
    });

    [Fact]
    public void InsertedImage_KeepsTheTextAroundIt() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.CaretPosition = fx.Editor.Document.ContentEnd;

        fx.View.InsertImage(Png(20, 20));

        Assert.Contains("hello world", TextOf(fx.Editor.Document));
        Assert.Single(ImagesOf(fx.Editor.Document));
    });

    [Fact]
    public void ImageInsertedOverASelection_ReplacesIt() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();

        fx.View.InsertImage(Png(20, 20));

        Assert.DoesNotContain("hello", TextOf(fx.Editor.Document));
        Assert.Single(ImagesOf(fx.Editor.Document));
    });

    [Fact]
    public void ReopenedNote_ShowsItsImage() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.View.InsertImage(Png(64, 32));
        fx.Note.FlushDocument();

        var reopened = new NoteColumnView { DataContext = fx.Note };
        reopened.EnsureLoaded();

        var image = Assert.Single(ImagesOf(reopened.Editor.Document));
        Assert.NotNull(image.Source);
        Assert.Equal(64, image.Width);
    });

    [Fact]
    public void HugeImages_StartAtASaneSize_AndNeverOverflowTheColumn() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);

        fx.View.InsertImage(Png(2000, 1000));
        Ui.Settle();

        var image = Assert.Single(ImagesOf(fx.Editor.Document));
        Assert.Equal(800, image.Width);
        Assert.Equal(400, image.Height);
        Assert.InRange(image.MaxWidth, 60, fx.Editor.ActualWidth);
    });

    [Fact]
    public void Undo_RemovesAPastedImage() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.View.InsertImage(Png(20, 20));

        ApplicationCommands.Undo.Execute(null, fx.Editor);

        Assert.Empty(ImagesOf(fx.Editor.Document));
    });

    [Fact]
    public void NoImagePasted_WhenTheStoreIsMissing() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        var stranded = new NoteViewModel { Body = Plain };
        var view = new NoteColumnView { DataContext = stranded };

        view.InsertImage(Png(10, 10));

        Assert.Empty(ImagesOf(view.Editor.Document));
    });

    // ----- Whole pipeline -----

    [Fact]
    public void EditsAndImages_SurviveSnapshotSaveAndReload() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);
        fx.Editor.SelectAll();
        Click(fx.Toolbar.BoldButton);
        fx.Editor.CaretPosition = fx.Editor.Document.ContentEnd;
        fx.Editor.AppendText(" and more");
        fx.View.InsertImage(Png(48, 24));

        var app = new AppViewModel(new AppConfig(), [fx.Workspace], 0, fx.Repo);
        var snapshot = SnapshotMapper.ToSnapshot(app);
        fx.Repo.Save(snapshot);

        var reloadedRepo = TestReam.Repo(fx.Repo.Root);
        var reloaded = SnapshotMapper.ToViewModel(reloadedRepo.Load(), new AppConfig(), reloadedRepo);
        var note = reloaded.Workspaces[1].Notes[0];
        var view = new NoteColumnView { DataContext = note };
        view.EnsureLoaded();

        var document = view.Editor.Document;
        Assert.Contains("and more", TextOf(document));
        Assert.Contains(RunsOf(document), r => r.FontWeight == FontWeights.Bold);
        var image = Assert.Single(ImagesOf(document));
        Assert.NotNull(image.Source);
        Assert.Equal(48, image.Width);
    });

    [Fact]
    public void EditorFocusRequest_MovesFocusIntoTheNote() => Ui.Run(() =>
    {
        using var fx = new EditorFixture(Plain);

        fx.Note.RequestEditorFocus();
        Ui.Settle();

        Assert.Same(fx.Editor, FocusManager.GetFocusedElement(fx.Window));
    });

    [Fact]
    public void ClosedView_StopsReactingToFocusRequests_AndKeepsItsEdits() => Ui.Run(() =>
    {
        var fx = new EditorFixture(Plain);
        fx.Editor.AppendText(" last words");
        var note = fx.Note;

        fx.Window.Close();
        Ui.Settle();
        note.RequestEditorFocus();

        Assert.Contains("last words", NoteContent.ToPlainText(note.Body));
        fx.Dir.Dispose();
    });
}
