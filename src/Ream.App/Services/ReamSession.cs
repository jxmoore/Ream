using System.Windows.Threading;
using Ream.App.ViewModels;
using Ream.Persistence.Storage;

namespace Ream.App.Services;

/// <summary>
/// One open ream: its repository and the coordinator that keeps it in step with the view models. The window and the
/// <see cref="AppViewModel"/> outlive it (opening another ream swaps the content in place), so the session also tells the
/// view model the ream's name and whether it has unsaved changes.
/// </summary>
internal sealed class ReamSession : IDisposable
{
    private readonly AppViewModel _app;

    public ReamSession(DocumentRepository repository, AppViewModel app, Dispatcher dispatcher, bool autoSave, bool startClean = true)
    {
        Repository = repository;
        _app = app;
        Coordinator = new PersistenceCoordinator(repository, app, dispatcher, autoSave, startClean);

        app.ReamName = repository.Name;
        app.ReamPath = repository.ReamPath;
        app.AutoSave = autoSave;
        app.HasUnsavedChanges = Coordinator.HasUnsavedChanges;
        Coordinator.UnsavedChangesChanged += OnUnsavedChanged;
    }

    public DocumentRepository Repository { get; }

    public PersistenceCoordinator Coordinator { get; }

    public string ReamPath => Repository.ReamPath;

    public string Name => Repository.Name;

    public bool HasUnsavedChanges => Coordinator.HasUnsavedChanges;

    /// <summary>Save as you go (on) or only when asked (off). Follows config.json.</summary>
    public bool AutoSave
    {
        get => Coordinator.AutoSave;
        set
        {
            Coordinator.AutoSave = value;
            _app.AutoSave = value;
        }
    }

    /// <summary>Writes the current state now. False if it failed (see <see cref="PersistenceCoordinator.LastError"/>).</summary>
    public bool Save() => Coordinator.Save();

    private void OnUnsavedChanged(bool unsaved) => _app.HasUnsavedChanges = unsaved;

    public void Dispose()
    {
        Coordinator.UnsavedChangesChanged -= OnUnsavedChanged;
        Coordinator.Dispose();
    }
}
