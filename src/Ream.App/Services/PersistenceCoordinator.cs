using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using Ream.App.ViewModels;
using Ream.Core.Abstractions;
using Ream.Core.Utilities;

namespace Ream.App.Services;

/// <summary>
/// Watches the view models and saves changes to storage: a short debounce coalesces bursts
/// (holding an arrow key, for instance) and writes happen off the UI thread, one at a time.
/// </summary>
internal sealed class PersistenceCoordinator : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly IDocumentRepository _repository;
    private readonly AppViewModel _app;
    private readonly DebouncedAction _debounce;
    private readonly object _queueGate = new();
    private Task _tail = Task.CompletedTask;

    public PersistenceCoordinator(IDocumentRepository repository, AppViewModel app, Dispatcher dispatcher)
    {
        _repository = repository;
        _app = app;
        _debounce = new DebouncedAction(SaveNow, SaveDelay, action => dispatcher.BeginInvoke(action));

        app.PropertyChanged += OnChanged;
        app.Workspaces.CollectionChanged += OnWorkspacesChanged;
        foreach (var workspace in app.Workspaces) Attach(workspace);

        MarkDirty();
    }

    /// <summary>The most recent failed save, if any. Saves keep being attempted after a failure.</summary>
    public Exception? LastError { get; private set; }

    public void MarkDirty() => _debounce.Trigger();

    /// <summary>Saves anything pending and waits for the write to finish. Call before exit.</summary>
    public void Flush()
    {
        _debounce.Flush();

        Task tail;
        lock (_queueGate) tail = _tail;
        tail.Wait(TimeSpan.FromSeconds(5));
    }

    private void SaveNow()
    {
        var snapshot = SnapshotMapper.ToSnapshot(_app);
        Enqueue(() => _repository.Save(snapshot));
    }

    private void Enqueue(Action work)
    {
        lock (_queueGate)
        {
            _tail = _tail.ContinueWith(_ =>
            {
                try
                {
                    work();
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = ex;
                    Debug.WriteLine($"Save failed: {ex}");
                }
            }, TaskScheduler.Default);
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void OnWorkspacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (WorkspaceViewModel workspace in e.OldItems) Detach(workspace);
        if (e.NewItems is not null)
            foreach (WorkspaceViewModel workspace in e.NewItems) Attach(workspace);
        MarkDirty();
    }

    private void OnNotesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (NoteViewModel note in e.OldItems) note.PropertyChanged -= OnChanged;
        if (e.NewItems is not null)
            foreach (NoteViewModel note in e.NewItems) note.PropertyChanged += OnChanged;
        MarkDirty();
    }

    private void Attach(WorkspaceViewModel workspace)
    {
        workspace.PropertyChanged += OnChanged;
        workspace.Notes.CollectionChanged += OnNotesChanged;
        foreach (var note in workspace.Notes) note.PropertyChanged += OnChanged;
    }

    private void Detach(WorkspaceViewModel workspace)
    {
        workspace.PropertyChanged -= OnChanged;
        workspace.Notes.CollectionChanged -= OnNotesChanged;
        foreach (var note in workspace.Notes) note.PropertyChanged -= OnChanged;
    }

    public void Dispose() => _debounce.Dispose();
}
