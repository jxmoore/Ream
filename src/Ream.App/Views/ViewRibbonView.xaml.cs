using System.Windows;
using System.Windows.Controls;
using Ream.App.ViewModels;

namespace Ream.App.Views;

/// <summary>The View tab of the ribbon: laid out like Word's own, plus Ream's own settings tacked on at the end. Its DataContext is the <c>SettingsViewModel</c>.</summary>
public partial class ViewRibbonView : UserControl
{
    private const int ZoomStep = 10;

    public ViewRibbonView() => InitializeComponent();

    /// <summary>The Read Mode tile: toggling the focused note's fullscreen + read-only lock is the app view model's job, not this tab's.</summary>
    public event Action? ReadModeRequested;

    /// <summary>The Draft tile, for the same reason.</summary>
    public event Action? DraftViewRequested;

    /// <summary>The Outline tile, for the same reason.</summary>
    public event Action? OutlineViewRequested;

    /// <summary>The Ruler tile, for the same reason (it toggles the focused note's own ruler, not a session setting).</summary>
    public event Action? RulerRequested;

    /// <summary>The New Note tile, for the same reason.</summary>
    public event Action? NewNoteRequested;

    /// <summary>The New Workspace tile, for the same reason.</summary>
    public event Action? NewWorkspaceRequested;

    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    private void OnReadModeClick(object sender, RoutedEventArgs e) => ReadModeRequested?.Invoke();

    private void OnDraftViewClick(object sender, RoutedEventArgs e) => DraftViewRequested?.Invoke();

    private void OnOutlineViewClick(object sender, RoutedEventArgs e) => OutlineViewRequested?.Invoke();

    private void OnRulerClick(object sender, RoutedEventArgs e) => RulerRequested?.Invoke();

    private void OnNewNoteClick(object sender, RoutedEventArgs e) => NewNoteRequested?.Invoke();

    private void OnNewWorkspaceClick(object sender, RoutedEventArgs e) => NewWorkspaceRequested?.Invoke();

    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        if (Settings is { } settings) settings.ZoomPercent = 100;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        if (Settings is { } settings) settings.ZoomPercent += ZoomStep; // SettingsViewModel clamps to 50-200
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        if (Settings is { } settings) settings.ZoomPercent -= ZoomStep;
    }
}
