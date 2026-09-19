using System.Windows.Controls;

namespace Ream.App.Views;

/// <summary>The panel the cogwheel opens: pick a theme, set how see-through the canvas is. Its DataContext is a SettingsViewModel.</summary>
public partial class SettingsFlyout : UserControl
{
    public SettingsFlyout()
    {
        InitializeComponent();
    }
}
