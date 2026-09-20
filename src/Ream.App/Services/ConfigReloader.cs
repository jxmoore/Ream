using System.Windows.Threading;
using Ream.App.ViewModels;
using Ream.Persistence.Storage;

namespace Ream.App.Services;

/// <summary>
/// Applies edits to config.json while the app runs: gaps, animation timing, keybindings and theme.
/// A file that can't be used is reported and the settings already in force stay, so a typo halfway
/// through an edit never breaks the running app or touches the user's file.
/// </summary>
internal sealed class ConfigReloader : IDisposable
{
    private readonly AppConfigStore _store;
    private readonly AppViewModel _app;
    private readonly ThemeService _theme;
    private readonly ConfigWatcher? _watcher;

    /// <param name="dispatcher">When given, the file is watched and reloaded on that thread; null means reload only on request (tests).</param>
    public ConfigReloader(AppConfigStore store, AppViewModel app, ThemeService theme, Dispatcher? dispatcher = null)
    {
        _store = store;
        _app = app;
        _theme = theme;

        if (dispatcher is not null)
            _watcher = new ConfigWatcher(store.Path, Reload, invoker: action => dispatcher.BeginInvoke(action));
    }

    public void Reload()
    {
        // A settings change made in the app comes back through the file watcher; the app already has it.
        if (_store.IsOurOwnLastWrite()) return;

        if (!_store.TryLoad(out var config, out var error))
        {
            _app.ConfigError = $"config.json: {error}";
            return;
        }

        _app.ConfigError = null;
        _app.Config = config;
        _theme.Apply(config);
    }

    public void Dispose() => _watcher?.Dispose();
}
