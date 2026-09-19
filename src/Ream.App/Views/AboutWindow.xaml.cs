using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Ream.App.Services;

namespace Ream.App.Views;

/// <summary>Name, version and where things live.</summary>
internal partial class AboutWindow : Window
{
    public AboutWindow(AboutInfo info)
    {
        InitializeComponent();
        DataContext = info;
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"Couldn't open {e.Uri}: {ex.Message}");
        }
    }
}
