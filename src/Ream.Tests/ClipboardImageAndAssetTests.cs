using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ream.Core.Models;
using Ream.Persistence.NoteFormat;
using Ream.Persistence.Storage;

namespace Ream.Tests;

public class ClipboardImageTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    private static BitmapSource Bitmap() =>
        BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);

    [Fact]
    public void PictureAlone_IsPastableAsAnImage() => Sta.Run(() =>
    {
        var data = new DataObject();
        data.SetImage(Bitmap());

        Assert.True(ClipboardImage.HasPastableImage(data));
        Assert.True(ClipboardImage.TryGetPng(data, out var png));
        Assert.Equal(PngSignature, png.Take(4));
        Assert.Equal(2, NoteImage.FromPng(png)!.PixelWidth);
    });

    [Fact]
    public void PictureWithText_IsLeftToTheNormalPaste() => Sta.Run(() =>
    {
        var data = new DataObject();
        data.SetImage(Bitmap());
        data.SetText("copied from a web page");

        Assert.False(ClipboardImage.HasPastableImage(data));
    });

    [Fact]
    public void TextOnly_IsNotAnImage() => Sta.Run(() =>
    {
        var data = new DataObject();
        data.SetText("hello");

        Assert.False(ClipboardImage.HasPastableImage(data));
        Assert.False(ClipboardImage.TryGetPng(data, out _));
    });

    [Fact]
    public void PngFormat_IsUsedAsIsSoTransparencyIsKept() => Sta.Run(() =>
    {
        byte[] original = NoteImage.EncodePng(Bitmap());
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(original));

        Assert.True(ClipboardImage.HasPastableImage(data));
        Assert.True(ClipboardImage.TryGetPng(data, out var png));
        Assert.Equal(original, png);
    });

    [Fact]
    public void NonPngBytesInThePngFormat_AreReencodedOrRejected() => Sta.Run(() =>
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]));

        Assert.False(ClipboardImage.TryGetPng(data, out _));
    });
}

public class AssetStoreTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private static WorkspaceSnapshot Workspace(string folder, params NoteSnapshot[] notes) =>
        new(Guid.NewGuid(), folder, folder, notes, null);

    private static NoteSnapshot Note() => new(Guid.NewGuid(), "n", "body", 0.5, false);

    [Fact]
    public void SavedAsset_CanBeFoundAgain()
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Docs"));
        var noteId = Guid.NewGuid();

        string name = repo.SaveAsset("ws-11111111", noteId, Png);
        string? path = repo.GetAssetPath("ws-11111111", noteId, name);

        Assert.EndsWith(".png", name);
        Assert.NotNull(path);
        Assert.Equal(Png, File.ReadAllBytes(path!));
        Assert.Contains(Path.Combine("ws-11111111", "assets", noteId.ToString("N")), path);
    }

    [Theory]
    [InlineData("..\\secret.png")]
    [InlineData("a/b.png")]
    [InlineData("..")]
    [InlineData("")]
    public void UnsafeAssetNames_AreRefused(string name)
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Docs"));

        Assert.Null(repo.GetAssetPath("ws-11111111", Guid.NewGuid(), name));
    }

    [Fact]
    public void UnsafeWorkspaceFolder_IsRefusedWhenSaving()
    {
        using var dir = new TempDir();
        var repo = new DocumentRepository(dir.Combine("Docs"));

        Assert.Throws<ArgumentException>(() => repo.SaveAsset("..\\outside", Guid.NewGuid(), Png));
    }

    [Fact]
    public void Assets_FollowTheirNoteToAnotherWorkspace()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var repo = new DocumentRepository(root);
        var note = Note();
        var source = Workspace("ws-aaaaaaaa", note);
        var target = Workspace("ws-bbbbbbbb");
        repo.Save(new DocumentSnapshot([source, target], null));
        string name = repo.SaveAsset("ws-aaaaaaaa", note.Id, Png);

        repo.Save(new DocumentSnapshot([source with { Notes = [] }, target with { Notes = [note] }], null));

        Assert.NotNull(repo.GetAssetPath("ws-bbbbbbbb", note.Id, name));
        Assert.False(Directory.Exists(Path.Combine(root, "ws-aaaaaaaa", "assets")));
    }

    [Fact]
    public void ImagePastedAfterTheNoteMoved_DoesNotStrandTheOlderImages()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var repo = new DocumentRepository(root);
        var note = Note();
        repo.Save(new DocumentSnapshot([Workspace("ws-aaaaaaaa", note), Workspace("ws-bbbbbbbb")], null));
        string first = repo.SaveAsset("ws-aaaaaaaa", note.Id, Png);

        string second = repo.SaveAsset("ws-bbbbbbbb", note.Id, Png);

        Assert.NotNull(repo.GetAssetPath("ws-bbbbbbbb", note.Id, first));
        Assert.NotNull(repo.GetAssetPath("ws-bbbbbbbb", note.Id, second));
        Assert.False(Directory.Exists(Path.Combine(root, "ws-aaaaaaaa", "assets", note.Id.ToString("N"))));
    }

    [Fact]
    public void ClosedNote_TakesItsImagesToTheTrash()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var repo = new DocumentRepository(root);
        var note = Note();
        var workspace = Workspace("ws-aaaaaaaa", note);
        repo.Save(new DocumentSnapshot([workspace], null));
        string name = repo.SaveAsset("ws-aaaaaaaa", note.Id, Png);

        repo.Save(new DocumentSnapshot([workspace with { Notes = [] }], null));

        string trashed = Path.Combine(root, ".trash", "ws-aaaaaaaa", "assets", note.Id.ToString("N"), name);
        Assert.True(File.Exists(trashed));
        Assert.Null(repo.GetAssetPath("ws-aaaaaaaa", note.Id, name));
    }

    [Fact]
    public void AssetsOnDisk_AreRecognisedAfterARestart()
    {
        using var dir = new TempDir();
        string root = dir.Combine("Docs");
        var note = Note();
        var source = Workspace("ws-aaaaaaaa", note);
        var target = Workspace("ws-bbbbbbbb");
        var first = new DocumentRepository(root);
        first.Save(new DocumentSnapshot([source, target], null));
        string name = first.SaveAsset("ws-aaaaaaaa", note.Id, Png);

        var restarted = new DocumentRepository(root);
        var loaded = restarted.Load();
        restarted.Save(new DocumentSnapshot(
            [loaded.Workspaces[0] with { Notes = [] }, loaded.Workspaces[1] with { Notes = loaded.Workspaces[0].Notes }], null));

        Assert.NotNull(restarted.GetAssetPath("ws-bbbbbbbb", note.Id, name));
    }
}
