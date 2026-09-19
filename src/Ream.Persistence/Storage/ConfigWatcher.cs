using Ream.Core.Utilities;

namespace Ream.Persistence.Storage;

/// <summary>
/// Reports (once, after things settle) that config.json was created, changed, renamed into place, or replaced.
/// Editors often save by writing a temp file and renaming it, so several events arrive for one save.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DebouncedAction _debounce;

    /// <param name="invoker">Marshals the callback onto another thread (e.g. the UI thread); null runs it on a pool thread.</param>
    public ConfigWatcher(string path, Action changed, TimeSpan? delay = null, Action<Action>? invoker = null)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string fileName = Path.GetFileName(path);
        Directory.CreateDirectory(directory);

        _debounce = new DebouncedAction(changed, delay ?? TimeSpan.FromMilliseconds(300), invoker);
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => _debounce.Trigger();

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
