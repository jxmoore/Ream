using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ream.App.ViewModels;

namespace Ream.App.Views;

/// <summary>A strip of workspace chips: click to switch, double-click (or Alt+Shift+R) to rename in place.</summary>
public partial class WorkspaceTabs : UserControl
{
    public WorkspaceTabs()
    {
        InitializeComponent();
    }

    private AppViewModel? App => DataContext as AppViewModel;

    private static WorkspaceViewModel? WorkspaceOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as WorkspaceViewModel;

    private void OnChipMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (WorkspaceOf(sender) is not { } workspace || App is not { } app) return;

        if (e.ClickCount >= 2) app.BeginRenameCommand.Execute(workspace);
        else app.SelectWorkspaceCommand.Execute(workspace);
        e.Handled = true;
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (WorkspaceOf(sender) is not { } workspace || App is not { } app) return;

        switch (e.Key)
        {
            case Key.Enter:
                app.CommitRenameCommand.Execute(workspace);
                e.Handled = true;
                break;
            case Key.Escape:
                app.CancelRenameCommand.Execute(workspace);
                e.Handled = true;
                break;
        }
    }

    // Clicking away keeps what was typed. Escape has already ended the rename, so this then does nothing.
    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        WorkspaceOf(sender)?.CommitRename();

    private void OnNameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;

        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            box.SelectAll();
        });
    }
}
