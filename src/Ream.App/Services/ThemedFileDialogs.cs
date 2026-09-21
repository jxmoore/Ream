using System.Windows;
using Ream.App.Services.FileBrowser;
using Ream.App.Views;

namespace Ream.App.Services;

/// <summary>The file pickers, drawn by Ream (<see cref="FileBrowserWindow"/>) so they carry the app's title bar and theme.</summary>
internal sealed class ThemedFileDialogs : IFileDialogs
{
    private readonly IFileSystem _fileSystem;
    private readonly Func<FileBrowserWindow, bool?> _show;

    /// <param name="fileSystem">The disk the dialog browses; tests pass an in-memory one.</param>
    /// <param name="show">How the window is put in front of the user; tests answer without showing it. Default: modal over the main window.</param>
    public ThemedFileDialogs(IFileSystem? fileSystem = null, Func<FileBrowserWindow, bool?>? show = null)
    {
        _fileSystem = fileSystem ?? new WindowsFileSystem();
        _show = show ?? ShowModal;
    }

    public string? PickNewReam(string suggestedName, string initialDirectory) =>
        Pick(BrowserMode.Save, "New ream", "Create", initialDirectory, suggestedName);

    public string? PickOpenReam(string initialDirectory) =>
        Pick(BrowserMode.Open, "Open ream", "Open", initialDirectory, null);

    public string? PickSaveAs(string suggestedName, string initialDirectory) =>
        Pick(BrowserMode.Save, "Save ream as", "Save", initialDirectory, suggestedName);

    private string? Pick(BrowserMode mode, string title, string acceptText, string initialDirectory, string? suggestedName)
    {
        var model = new FileBrowserModel(_fileSystem, mode, initialDirectory, suggestedName);
        var window = new FileBrowserWindow(model, title, acceptText);
        return _show(window) == true ? window.SelectedPath : null;
    }

    private static bool? ShowModal(FileBrowserWindow window)
    {
        if (Application.Current?.MainWindow is { IsVisible: true } owner) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return window.ShowDialog();
    }
}
