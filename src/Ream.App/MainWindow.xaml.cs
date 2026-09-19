using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private readonly WheelAccumulator _tiltWheel = new();

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
    }

    // WPF has no event for a horizontal (tilt) wheel, so read the raw message.
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (MouseTilt.TryGetDelta(message, wParam.ToInt64(), out int delta))
        {
            _viewModel.FocusNoteBy(_tiltWheel.Add(delta));
            handled = true;
        }
        return IntPtr.Zero;
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
            _viewModel.FocusNoteBy(-_rowWheel.Add(e.Delta));
            e.Handled = true;
        }

        base.OnPreviewMouseWheel(e);
    }
}
