using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Ream.Core.Models;

namespace Ream.App.Services;

/// <summary>Swaps the app's color palette at runtime, following the Windows setting when the theme is "system".</summary>
internal sealed class ThemeService : IDisposable
{
    private static readonly Uri LightPalette = new("/Ream.App;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("/Ream.App;component/Themes/Dark.xaml", UriKind.Relative);

    private readonly Application _application;
    private readonly Func<bool> _systemIsLight;
    private Dispatcher? _watchDispatcher;
    private string? _setting;
    private bool? _isLight;

    /// <param name="systemIsLight">Reads the Windows app-theme setting; injectable so tests need no registry.</param>
    public ThemeService(Application application, Func<bool>? systemIsLight = null)
    {
        _application = application;
        _systemIsLight = systemIsLight ?? ReadSystemIsLight;
    }

    /// <summary>Raised (with the new value of <see cref="IsLight"/>) whenever the palette actually changes.</summary>
    public event Action<bool>? Changed;

    public bool IsLight => _isLight ?? false;

    public void Apply(string? setting)
    {
        _setting = setting;
        bool light = ThemeChoice.IsLight(setting, _systemIsLight());
        if (light == _isLight) return;

        var palette = new ResourceDictionary { Source = light ? LightPalette : DarkPalette };
        var merged = _application.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = palette;
        else merged.Insert(0, palette);

        _isLight = light;
        Changed?.Invoke(light);
    }

    /// <summary>Re-resolves the current setting, e.g. after Windows switches between light and dark.</summary>
    public void Refresh() => Apply(_setting);

    /// <summary>Starts following Windows theme changes (only matters while the setting is "system").</summary>
    public void WatchSystemChanges(Dispatcher dispatcher)
    {
        _watchDispatcher = dispatcher;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        _watchDispatcher?.BeginInvoke(Refresh);
    }

    private static bool ReadSystemIsLight()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is not int flag || flag != 0;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_watchDispatcher is not null)
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
