using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Persistence.NoteFormat;

namespace Ream.Tests;

/// <summary>A draft says so (dashed outline + "Draft" pill) until it has something in it.</summary>
public class DraftIndicatorTests
{
    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 90; pixels[i + 2] = 40; pixels[i + 3] = 255; }
        return NoteImage.EncodePng(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4));
    }

    private static (Border Pill, Rectangle Outline) Cues(NoteColumnView view) =>
        ((Border)view.FindName("DraftPill"), (Rectangle)view.FindName("DraftOutline"));

    private static NoteColumnView Host(NoteViewModel note, out Window window)
    {
        var view = new NoteColumnView { DataContext = note };
        window = Ui.Show(view);
        view.EnsureLoaded();
        Ui.Settle();
        return view;
    }

    [Fact]
    public void ADraft_ShowsThePillAndTheDashedOutline() => Ui.Run(() =>
    {
        var note = new NoteViewModel { Title = "Untitled 1", IsDraft = true };
        var view = Host(note, out var window);
        try
        {
            var (pill, outline) = Cues(view);

            Assert.Equal(Visibility.Visible, pill.Visibility);
            Assert.Equal(Visibility.Visible, outline.Visibility);
            Assert.NotNull(outline.StrokeDashArray);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ARealNote_ShowsNeither() => Ui.Run(() =>
    {
        var note = new NoteViewModel { Title = "Plans" };
        var view = Host(note, out var window);
        try
        {
            var (pill, outline) = Cues(view);

            Assert.Equal(Visibility.Collapsed, pill.Visibility);
            Assert.Equal(Visibility.Collapsed, outline.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TypingInADraft_TakesTheCuesAway() => Ui.Run(() =>
    {
        var note = new NoteViewModel { Title = "Untitled 1", IsDraft = true };
        var view = Host(note, out var window);
        try
        {
            view.Editor.AppendText("   ");
            Ui.Settle();
            Assert.True(note.IsDraft); // spaces are still blank

            view.Editor.AppendText("hello");
            Ui.Settle();

            Assert.False(note.IsDraft);
            var (pill, outline) = Cues(view);
            Assert.Equal(Visibility.Collapsed, pill.Visibility);
            Assert.Equal(Visibility.Collapsed, outline.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PastingAPictureIntoADraft_KeepsIt() => Ui.Run(() =>
    {
        using var fx = new EditorFixture();
        fx.Note.IsDraft = true;

        fx.View.InsertImage(Png(20, 20));
        Ui.Settle();

        Assert.False(fx.Note.IsDraft);
    });

    [Fact]
    public void NamingADraft_KeepsItToo()
    {
        var note = new NoteViewModel { Title = "Untitled 1", IsDraft = true };

        note.BeginTitleEdit();
        note.EditTitle = "Plans";
        note.CommitTitleEdit();

        Assert.False(note.IsDraft);
    }

    [Fact]
    public void SubmittingTheDefaultTitleUnchanged_DoesNotKeepADraft()
    {
        var note = new NoteViewModel { Title = "Untitled 1", IsDraft = true };

        note.BeginTitleEdit();
        note.CommitTitleEdit();

        Assert.True(note.IsDraft);
    }
}
