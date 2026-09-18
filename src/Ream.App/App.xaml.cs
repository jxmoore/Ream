using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ream.App.Fake;
using Ream.Core.Models;

namespace Ream.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new AppConfig());
                services.AddSingleton(sp => SampleData.Create(sp.GetRequiredService<AppConfig>()));
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.StartAsync().GetAwaiter().GetResult();
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
