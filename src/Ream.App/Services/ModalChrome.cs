using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Ream.App.Services;

/// <summary>What the shared modal window template (Themes/Controls.xaml, ModalWindowStyle) needs from code.</summary>
public static class ModalChrome
{
    /// <summary>The title bar's Close button: closes the window it is given as the command parameter.</summary>
    public static ICommand CloseCommand { get; } = new RelayCommand<Window>(window => window?.Close());
}
