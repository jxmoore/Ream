using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.ViewModels;

namespace Ream.App.Views;

/// <summary>
/// The View tab's Theme button opens this: Layout, Theme and Opacity, all editing the same <see cref="SettingsViewModel"/>
/// the three separate ribbon groups used to. Themed like Help/About (<c>ModalWindowStyle</c>), not a ribbon dropdown.
/// </summary>
internal partial class ThemeModal : Window
{
    private static readonly double[] GapPresets = [8, 12, 16, 20, 24, 28, 32, 40, 48, 60];
    private static readonly int[] OpacityPresets = [0, 10, 25, 50, 75, 90, 100];

    public ThemeModal(SettingsViewModel settings)
    {
        InitializeComponent();
        DataContext = settings;

        GapBox.ItemsSource = GapPresets;
        CanvasOpacityBox.ItemsSource = OpacityPresets;
        NoteOpacityBox.ItemsSource = OpacityPresets;
    }

    /// <summary>Enter commits whatever was typed immediately, the same as tabbing away would (LostFocus is the binding's own trigger).</summary>
    private void OnNumberBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox box) return;

        box.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings) return;
        if (((FrameworkElement)sender).DataContext is not ThemeOption option) return;

        settings.SelectThemeCommand.Execute(option);
    }
}
