using System.Windows;
using System.Windows.Controls;

namespace Ream.App.Views;

/// <summary>
/// The File tab of the ribbon. Its workspace list is bound to the app (so it is always current); Help and About
/// are raised as events because opening a window is the main window's job.
/// </summary>
public partial class FileRibbonView : UserControl
{
    public FileRibbonView() => InitializeComponent();

    public event Action? HelpRequested;

    public event Action? AboutRequested;

    private void OnHelp(object sender, RoutedEventArgs e) => HelpRequested?.Invoke();

    private void OnAbout(object sender, RoutedEventArgs e) => AboutRequested?.Invoke();
}
