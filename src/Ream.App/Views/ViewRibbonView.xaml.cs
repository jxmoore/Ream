using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Ream.App.ViewModels;

namespace Ream.App.Views;

/// <summary>The View tab of the ribbon: laid out like Word's own, plus Ream's own settings tacked on at the end. Its DataContext is the <c>SettingsViewModel</c>.</summary>
public partial class ViewRibbonView : UserControl
{
    private static readonly int[] ZoomPresets = [50, 75, 100, 125, 150, 175, 200];

    public ViewRibbonView() => InitializeComponent();

    /// <summary>The Read Mode tile: toggling the focused note's fullscreen is the app view model's job, not this tab's.</summary>
    public event Action? ReadModeRequested;

    /// <summary>The New Note tile, for the same reason.</summary>
    public event Action? NewNoteRequested;

    /// <summary>The New Workspace tile, for the same reason.</summary>
    public event Action? NewWorkspaceRequested;

    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    private void OnReadModeClick(object sender, RoutedEventArgs e) => ReadModeRequested?.Invoke();

    private void OnNewNoteClick(object sender, RoutedEventArgs e) => NewNoteRequested?.Invoke();

    private void OnNewWorkspaceClick(object sender, RoutedEventArgs e) => NewWorkspaceRequested?.Invoke();

    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        if (Settings is { } settings) settings.ZoomPercent = 100;
    }

    /// <summary>The big Zoom tile: a menu of levels, like Word's own Zoom dialog reduced to its presets.</summary>
    private void OnZoomClick(object sender, RoutedEventArgs e)
    {
        if (Settings is not { } settings) return;

        var menu = new ContextMenu { PlacementTarget = ZoomButton, Placement = PlacementMode.Bottom };
        foreach (int percent in ZoomPresets)
        {
            var item = new MenuItem { Header = $"{percent}%", IsCheckable = true, IsChecked = settings.ZoomPercent == percent };
            item.Click += (_, _) => settings.ZoomPercent = percent;
            menu.Items.Add(item);
        }
        ZoomButton.ContextMenu = menu; // so it can be found again (tests, and if the menu needs rebuilding later)
        menu.IsOpen = true;
    }
}
