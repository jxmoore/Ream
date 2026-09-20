using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Abstractions;
using Ream.Core.Models;
using Ream.Persistence;
using Ream.Persistence.Storage;

namespace Ream.App;

public partial class App : Application
{
    private IHost? _host;
    private PersistenceCoordinator? _persistence;
    private ThemeService? _theme;
    private SettingsViewModel? _settings;
    private ConfigReloader? _reloader;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The test harness hosts this class only for its resources and must never run the real app
        // (real config, real documents, a real window). See Ream.Tests/Ui.cs.
        if (AppContext.TryGetSwitch("Ream.SkipStartup", out bool skip) && skip) return;

        try
        {
            var paths = AppPaths.Resolve(ParseHome(e.Args));
            var store = new AppConfigStore(paths.ConfigFile);
            var config = store.Load(paths.DefaultDocumentsRoot);

            // Before any window exists, so nothing is ever drawn in the wrong palette.
            _theme = new ThemeService(this);
            _theme.Apply(config);

            string documentsRoot = string.IsNullOrWhiteSpace(config.DocumentsRoot)
                ? paths.DefaultDocumentsRoot
                : config.DocumentsRoot;

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(config);
                    services.AddSingleton(_ => new DocumentRepository(documentsRoot));
                    services.AddSingleton<IDocumentRepository>(sp => sp.GetRequiredService<DocumentRepository>());
                    services.AddSingleton<IAssetStore>(sp => sp.GetRequiredService<DocumentRepository>());
                    services.AddSingleton(sp => LoadOrSeed(
                        sp.GetRequiredService<IDocumentRepository>(), config, sp.GetRequiredService<IAssetStore>()));
                    services.AddSingleton(sp => new PersistenceCoordinator(
                        sp.GetRequiredService<IDocumentRepository>(),
                        sp.GetRequiredService<AppViewModel>(),
                        Dispatcher));
                    services.AddSingleton(sp => new SettingsViewModel(
                        sp.GetRequiredService<AppViewModel>(), _theme, store, Dispatcher));
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.StartAsync().GetAwaiter().GetResult();

            var window = _host.Services.GetRequiredService<MainWindow>();
            _persistence = _host.Services.GetRequiredService<PersistenceCoordinator>();
            _settings = _host.Services.GetRequiredService<SettingsViewModel>();

            window.FollowTheme(_theme);
            window.About = AboutInfo.Create(documentsRoot, paths.ConfigFile);
            _reloader = new ConfigReloader(store, _host.Services.GetRequiredService<AppViewModel>(), _theme, Dispatcher);

            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ream couldn't start.\n\n{ex.Message}",
                "Ream",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _reloader?.Dispose();
        _settings?.Flush();
        _settings?.Dispose();
        _persistence?.Flush();

        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static AppViewModel LoadOrSeed(IDocumentRepository repository, AppConfig config, IAssetStore assets)
    {
        var snapshot = repository.Load();
        return snapshot.IsFirstRun
            ? SeedData.CreateWelcome(config, assets)
            : SnapshotMapper.ToViewModel(snapshot, config, assets);
    }

    /// <summary>Optional `--home &lt;dir&gt;` keeps config and documents under one folder (dev and testing).</summary>
    private static string? ParseHome(string[] args)
    {
        int i = Array.IndexOf(args, "--home");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
