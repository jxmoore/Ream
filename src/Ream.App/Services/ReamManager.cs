using System.Windows.Threading;
using Ream.App.ViewModels;
using Ream.Core.Models;
using Ream.Persistence.Storage;

namespace Ream.App.Services;

/// <summary>
/// Creates, opens, saves and copies reams while the app is running. The window and the <see cref="AppViewModel"/> stay put;
/// what is *in* them is swapped in place, one <see cref="ReamSession"/> at a time. Every question (which file? save first?)
/// goes through <see cref="IFileDialogs"/> and <see cref="IUserPrompts"/> so it can be tested without any UI.
/// </summary>
internal sealed partial class ReamManager
{
    private readonly AppViewModel _app;
    private readonly AppConfigStore? _store;
    private readonly IFileDialogs _dialogs;
    private readonly IUserPrompts _prompts;
    private readonly Dispatcher _dispatcher;
    private readonly string _configFilePath;

    public ReamManager(
        ReamSession initial,
        AppViewModel app,
        AppConfigStore? store,
        IFileDialogs dialogs,
        IUserPrompts prompts,
        Dispatcher dispatcher,
        string configFilePath)
    {
        Current = initial;
        _app = app;
        _store = store;
        _dialogs = dialogs;
        _prompts = prompts;
        _dispatcher = dispatcher;
        _configFilePath = configFilePath;
    }

    /// <summary>The ream that is open now.</summary>
    public ReamSession Current { get; private set; }

    // ----- New -----

    /// <summary>Asks where, creates a ream there (the tutorial, or empty if config says so) and switches to it. False if cancelled or refused.</summary>
    public bool NewReam()
    {
        string? picked = _dialogs.PickNewReam(SuggestedName("Untitled"), InitialDirectory());
        if (picked is null) return false;

        string? path = FreePath(picked, "New ream");
        if (path is null) return false;

        if (!ConfirmLeave()) return false;

        try
        {
            var created = ReamLauncher.Create(path, _app.Config, _configFilePath);
            return SwapTo(created.Repository, created.Snapshot);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _prompts.ShowError("New ream", $"Ream couldn't create \"{path}\".\n\n{ex.Message}");
            return false;
        }
    }

    // ----- Leaving the open ream (New, Open, closing the window) -----

    /// <summary>
    /// Whether it is fine to leave the open ream now. With auto-save on, pending changes are written first. With it off, unsaved
    /// changes get the Save / Don't save / Cancel question. False means stay.
    /// </summary>
    public bool ConfirmLeave()
    {
        if (Current.AutoSave)
        {
            Current.Coordinator.Flush();
            return true;
        }

        Current.Coordinator.Evaluate(); // a change in the last moment counts too
        if (!Current.HasUnsavedChanges) return true;

        switch (_prompts.AskSaveChanges(Current.Name))
        {
            case SaveChoice.Save:
                if (Current.Save()) return true;
                _prompts.ShowError("Save", $"Ream couldn't save \"{Current.Name}\".\n\n{Current.Coordinator.LastError?.Message}");
                return false;
            case SaveChoice.DontSave:
                return true;
            default:
                return false;
        }
    }

    // ----- Shared -----

    /// <summary>Puts a loaded ream in place of the open one. The old session stops first so nothing is ever written into the wrong ream.</summary>
    /// <param name="keepPosition">Stay on the workspace and note you were on (Save As) instead of starting at the first note (opening a ream).</param>
    private bool SwapTo(DocumentRepository repository, DocumentSnapshot snapshot, bool keepPosition = false)
    {
        Current.Dispose();
        SnapshotMapper.LoadInto(_app, snapshot, repository, startAtFirstNote: !keepPosition);
        Current = new ReamSession(repository, _app, _dispatcher, _app.Config.AutoSave);

        RecordLastReam(repository.ReamPath);
        _app.RequestEditorFocus();
        return true;
    }

    /// <summary>Remembers this ream in config.json so the next launch reopens it.</summary>
    public void RecordLastReam(string reamPath)
    {
        if (string.Equals(_app.Config.LastReam, reamPath, StringComparison.OrdinalIgnoreCase)) return;

        _app.Config = _app.Config.WithLastReam(reamPath);
        if (_store is null) return;

        if (!_store.Update(root => root["lastReam"] = reamPath, out string? error))
            _app.ConfigError = $"Couldn't remember the last ream: {error}";
    }

    /// <summary>
    /// Turns what the dialog returned into a path a new ream can go at, or shows why not and returns null: it must be a .ream
    /// with a valid name, in a folder that exists, and must not land on an existing ream or its data folder.
    /// </summary>
    private string? FreePath(string picked, string title)
    {
        string path = picked.EndsWith(ReamPaths.Extension, StringComparison.OrdinalIgnoreCase) ? picked : picked + ReamPaths.Extension;

        if (!ReamPaths.IsReamFile(path) || !ReamPaths.IsValidName(ReamPaths.NameOf(path)))
        {
            _prompts.ShowError(title, $"\"{Path.GetFileName(path)}\" can't be used as a ream name.");
            return null;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null || !Directory.Exists(directory))
        {
            _prompts.ShowError(title, $"The folder \"{directory}\" doesn't exist.");
            return null;
        }

        if (ReamPaths.IsOccupied(path))
        {
            _prompts.ShowError(title, $"There is already a ream called \"{ReamPaths.NameOf(path)}\" there. Choose another name, or use Open to open it.");
            return null;
        }

        return Path.GetFullPath(path);
    }

    private string InitialDirectory() => Path.GetDirectoryName(Current.ReamPath) ?? "";

    private string SuggestedName(string baseName) =>
        Path.GetFileName(ReamPaths.UniquePath(InitialDirectory() is { Length: > 0 } dir && Directory.Exists(dir) ? dir : Path.GetTempPath(), baseName));

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException;
}
