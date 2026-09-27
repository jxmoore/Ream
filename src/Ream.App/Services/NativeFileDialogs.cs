using Microsoft.Win32;

namespace Ream.App.Services;

/// <summary>
/// The file pickers: Windows' own native Open/Save dialogs, not a custom window of Ream's own. There is no supported way to
/// paint Ream's own theme onto the native dialog (it is rendered by shell32/comdlg32 using the OS's own visual styles, not
/// anything an app can inject brushes or fonts into) - the only lever is Windows' own light/dark toggle via undocumented,
/// version-fragile DWM/uxtheme calls, which wouldn't carry Ream's actual palette anyway. Better to just be the real thing:
/// the exact dialog, icons and behaviour every other Windows app already gives people, for free and for good.
/// </summary>
internal sealed class NativeFileDialogs : IFileDialogs
{
    private const string ReamFilter = "Ream files (*.ream)|*.ream|All files (*.*)|*.*";

    private readonly Func<OpenFileDialog, bool?> _showOpen;
    private readonly Func<SaveFileDialog, bool?> _showSave;

    /// <param name="showOpen">How the Open dialog is shown; tests answer without a real dialog. Default: <see cref="OpenFileDialog.ShowDialog()"/>.</param>
    /// <param name="showSave">Same, for the Save dialog behind New and Save As.</param>
    public NativeFileDialogs(Func<OpenFileDialog, bool?>? showOpen = null, Func<SaveFileDialog, bool?>? showSave = null)
    {
        _showOpen = showOpen ?? (dialog => dialog.ShowDialog());
        _showSave = showSave ?? (dialog => dialog.ShowDialog());
    }

    public string? PickNewReam(string suggestedName, string initialDirectory) =>
        PickSave("New ream", suggestedName, initialDirectory);

    public string? PickSaveAs(string suggestedName, string initialDirectory) =>
        PickSave("Save ream as", suggestedName, initialDirectory);

    public string? PickOpenReam(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open ream",
            Filter = ReamFilter,
            InitialDirectory = initialDirectory,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };
        return _showOpen(dialog) == true ? dialog.FileName : null;
    }

    private string? PickSave(string title, string suggestedName, string initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = ReamFilter,
            DefaultExt = "ream",
            AddExtension = true,
            FileName = suggestedName,
            InitialDirectory = initialDirectory,
            CheckPathExists = true,
            // Ream never overwrites - ReamManager.FreePath rejects an occupied path with its own message and lets the
            // user pick again, so Windows' own "replace it?" prompt would just be a confusing extra step before that.
            OverwritePrompt = false,
        };
        return _showSave(dialog) == true ? dialog.FileName : null;
    }
}
