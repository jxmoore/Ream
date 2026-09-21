using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using Ream.App.ViewModels;
using Ream.Core.Abstractions;
using Ream.Core.Models;
using Ream.Core.Utilities;

namespace Ream.App.Services;

/// <summary>
/// Keeps one open ream and its storage in step. Changes are noticed on the view models; after a short quiet period
/// (holding an arrow key, say, is one burst) the state is snapshotted on the UI thread and compared with what was last
/// saved (<see cref="HasUnsavedChanges"/>, by content only: moving around is not an edit). With auto-save on the snapshot
/// is then written off the UI thread, one write at a time; with it off nothing is written until <see cref="Save"/>.
/// </summary>
internal sealed class PersistenceCoordinator : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly IDocumentRepository _repository;
    private readonly AppViewModel _app;
    private readonly Dispatcher _dispatcher;
    private readonly DebouncedAction _debounce;
    private readonly object _queueGate = new();
    private Task _tail = Task.CompletedTask;

    private bool _autoSave;
    private bool _disposed;
    private string _savedFingerprint;
    private string _currentFingerprint;

    /// <param name="startClean">
    /// True (the usual case) when the app state was just loaded from the repository, so it counts as saved.
    /// False for state that only exists in memory: it counts as unsaved until the first save.
    /// </param>
    public PersistenceCoordinator(IDocumentRepository repository, AppViewModel app, Dispatcher dispatcher, bool autoSave = true, bool startClean = true)
    {
        _repository = repository;
        _app = app;
        _dispatcher = dispatcher;
        _autoSave = autoSave;
        _debounce = new DebouncedAction(OnQuiet, SaveDelay, action => dispatcher.BeginInvoke(action));

        _currentFingerprint = SnapshotFingerprint.Of(SnapshotMapper.ToSnapshot(app));
        _savedFingerprint = startClean ? _currentFingerprint : "";
        HasUnsavedChanges = _currentFingerprint != _savedFingerprint;

        app.PropertyChanged += OnChanged;
        app.Workspaces.CollectionChanged += OnWorkspacesChanged;
        foreach (var workspace in app.Workspaces) Attach(workspace);

        MarkDirty();
    }

    /// <summary>The most recent failed save, if any. Saves keep being attempted after a failure.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>The ream's content differs from what was last written. Moving focus or switching workspace does not count.</summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="HasUnsavedChanges"/> flips.</summary>
    public event Action<bool>? UnsavedChangesChanged;

    /// <summary>Write changes as they happen. Turning it on saves whatever is pending straight away.</summary>
    public bool AutoSave
    {
        get => _autoSave;
        set
        {
            if (_autoSave == value) return;
            _autoSave = value;
            if (value) MarkDirty();
        }
    }

    public void MarkDirty() => _debounce.Trigger();

    /// <summary>Works out whether anything changed since the last save right now, without waiting for the quiet period.</summary>
    public void Evaluate() => Evaluate(SnapshotMapper.ToSnapshot(_app));

    /// <summary>
    /// Saves now, whether or not auto-save is on, and waits for the write. Returns false (and sets <see cref="LastError"/>)
    /// if it failed. Call on the UI thread.
    /// </summary>
    public bool Save()
    {
        var snapshot = SnapshotMapper.ToSnapshot(_app);
        string fingerprint = Evaluate(snapshot);

        Task tail = Enqueue(snapshot, fingerprint, postSaved: false);
        tail.Wait(TimeSpan.FromSeconds(30));

        if (LastError is not null) return false;
        MarkSaved(fingerprint);
        return true;
    }

    /// <summary>For exit: with auto-save on, saves anything pending and waits for the write. With it off, saves nothing (that is the point).</summary>
    public void Flush()
    {
        if (!_autoSave) return;

        _debounce.Flush();

        Task tail;
        lock (_queueGate) tail = _tail;
        tail.Wait(TimeSpan.FromSeconds(5));
    }

    private void OnQuiet()
    {
        if (_disposed) return;

        var snapshot = SnapshotMapper.ToSnapshot(_app);
        string fingerprint = Evaluate(snapshot);
        if (_autoSave) Enqueue(snapshot, fingerprint, postSaved: true);
    }

    private string Evaluate(DocumentSnapshot snapshot)
    {
        string fingerprint = SnapshotFingerprint.Of(snapshot);
        _currentFingerprint = fingerprint;
        SetUnsaved(fingerprint != _savedFingerprint);
        return fingerprint;
    }

    private void MarkSaved(string fingerprint)
    {
        _savedFingerprint = fingerprint;
        SetUnsaved(_currentFingerprint != _savedFingerprint);
    }

    private void SetUnsaved(bool unsaved)
    {
        if (HasUnsavedChanges == unsaved) return;
        HasUnsavedChanges = unsaved;
        UnsavedChangesChanged?.Invoke(unsaved);
    }

    private Task Enqueue(DocumentSnapshot snapshot, string fingerprint, bool postSaved)
    {
        lock (_queueGate)
        {
            _tail = _tail.ContinueWith(_ =>
            {
                try
                {
                    _repository.Save(snapshot);
                    LastError = null;
                    if (postSaved) _dispatcher.BeginInvoke(() => MarkSaved(fingerprint));
                }
                catch (Exception ex)
                {
                    LastError = ex;
                    Debug.WriteLine($"Save failed: {ex}");
                }
            }, TaskScheduler.Default);
            return _tail;
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

    /// <summary>Stops watching the view models (they outlive the ream: opening another one keeps the same AppViewModel).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _app.PropertyChanged -= OnChanged;
        _app.Workspaces.CollectionChanged -= OnWorkspacesChanged;
        foreach (var workspace in _app.Workspaces) Detach(workspace);
        _debounce.Dispose();
    }
}
