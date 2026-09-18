using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Ream.App.Input;
using Ream.App.ViewModels;
using Ream.Core.Utilities;

namespace Ream.App;

public partial class MainWindow : Window
{
    private readonly AppViewModel _viewModel;
    private readonly WheelAccumulator _workspaceWheel = new();
    private readonly WheelAccumulator _rowWheel = new();

    public MainWindow(AppViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        KeyBindingsRegistry.Apply(this, viewModel.Config.Keybindings, viewModel.Actions);

        // The new note's view may not exist yet when focus is requested, so wait until layout has caught up.
        viewModel.FocusEditorRequested += () => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => viewModel.CurrentWorkspace.FocusedNote?.RequestEditorFocus());
        Loaded += (_, _) => viewModel.RequestEditorFocus();
    }

    // Plain wheel is deliberately left alone so it scrolls the note under the cursor.
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            _viewModel.SwitchWorkspace(-_workspaceWheel.Add(e.Delta));
            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewModel.CurrentWorkspace.FocusBy(-_rowWheel.Add(e.Delta));
            e.Handled = true;
        }

        base.OnPreviewMouseWheel(e);
    }
}
