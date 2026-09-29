using System.Windows;
using System.Windows.Controls;
using Ream.App.ViewModels;

namespace Ream.App.Views;

/// <summary>
/// The Show group's Navigation Pane. Its DataContext is a <see cref="NavigationPaneViewModel"/> (set once by
/// MainWindow, not per instance like the note views) - everything here just relays a click into it.
/// </summary>
public partial class NavigationPaneView : UserControl
{
    public NavigationPaneView() => InitializeComponent();

    private NavigationPaneViewModel? Pane => DataContext as NavigationPaneViewModel;

    private void OnScopeChecked(object sender, RoutedEventArgs e)
    {
        if (Pane is not { } pane || sender is not RadioButton { Tag: string tag }) return;
        pane.SearchScope = Enum.Parse<NavigationSearchScope>(tag);
    }

    private void OnHeadingClick(object sender, RoutedEventArgs e)
    {
        if (Pane is not { } pane || ((FrameworkElement)sender).DataContext is not HeadingEntry entry) return;
        pane.JumpToHeading(entry);
    }

    private void OnNoteClick(object sender, RoutedEventArgs e)
    {
        if (Pane is not { } pane || ((FrameworkElement)sender).DataContext is not NoteEntry entry) return;
        pane.JumpToNote(entry);
    }

    private void OnWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (Pane is not { } pane || ((FrameworkElement)sender).DataContext is not WorkspaceEntry entry) return;
        pane.JumpToWorkspace(entry);
    }

    private void OnResultClick(object sender, RoutedEventArgs e)
    {
        if (Pane is not { } pane || ((FrameworkElement)sender).DataContext is not SearchResult entry) return;
        pane.JumpToResult(entry);
    }
}
