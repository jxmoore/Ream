using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    /// <summary>The One Page tile, for the same reason (it follows focus across the whole app, not this tab's job).</summary>
    public event Action? OnePageRequested;

    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    private void OnReadModeClick(object sender, RoutedEventArgs e) => ReadModeRequested?.Invoke();

    private void OnDraftViewClick(object sender, RoutedEventArgs e) => DraftViewRequested?.Invoke();

    private void OnOutlineViewClick(object sender, RoutedEventArgs e) => OutlineViewRequested?.Invoke();

    private void OnRulerClick(object sender, RoutedEventArgs e) => RulerRequested?.Invoke();

    private void OnOnePageClick(object sender, RoutedEventArgs e) => OnePageRequested?.Invoke();

    /// <summary>Switch Notes: a themed menu of the current workspace's notes, like Word's own Switch Windows reduced to one window's worth of documents.</summary>
    private void OnSwitchNotesClick(object sender, RoutedEventArgs e)
    {
        if (Settings is not { } settings) return;

        var workspace = settings.App.CurrentWorkspace;
        var menu = new ContextMenu { PlacementTarget = SwitchNotesButton, Placement = PlacementMode.Bottom };
        foreach (var note in workspace.Notes)
        {
            var item = new MenuItem { Header = note.DisplayTitle, IsCheckable = true, IsChecked = note.IsFocused };
            item.Click += (_, _) =>
            {
                workspace.SetFocus(workspace.Notes.IndexOf(note));
                settings.App.RequestEditorFocus();
            };
            menu.Items.Add(item);
        }
        SwitchNotesButton.ContextMenu = menu; // so it can be found again (tests, and if the menu needs rebuilding later)
        menu.IsOpen = true;
    }

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
