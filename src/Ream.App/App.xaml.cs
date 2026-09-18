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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var paths = AppPaths.Resolve(ParseHome(e.Args));
            var config = new AppConfigStore(paths.ConfigFile).Load(paths.DefaultDocumentsRoot);
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
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.StartAsync().GetAwaiter().GetResult();

            var window = _host.Services.GetRequiredService<MainWindow>();
            _persistence = _host.Services.GetRequiredService<PersistenceCoordinator>();
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
