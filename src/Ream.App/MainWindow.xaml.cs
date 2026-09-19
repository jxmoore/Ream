using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ream.App.Input;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Utilities;

namespace Ream.App;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;

    private readonly AppViewModel _viewModel;
    private readonly WheelAccumulator _workspaceWheel = new();
    private readonly WheelAccumulator _rowWheel = new();
    private readonly WheelAccumulator _tiltWheel = new();
    private readonly List<InputBinding> _configuredBindings = [];
    private readonly FullscreenController _fullscreen;
    private bool _titleBarIsLight;

    public MainWindow(AppViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _fullscreen = new FullscreenController(new WindowFrame(this));
        viewModel.AppFullscreenToggleRequested += _fullscreen.Toggle;

        ApplyKeyBindings();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The new note's view may not exist yet when focus is requested, so wait until layout has caught up.
        viewModel.FocusEditorRequested += () => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => viewModel.CurrentWorkspace.FocusedNote?.RequestEditorFocus());
        Loaded += (_, _) => viewModel.RequestEditorFocus();
    }

    /// <summary>Makes the title bar match the palette (Windows 10 20H1 and later; ignored where unsupported).</summary>
    public void ApplyTitleBarTheme(bool isLight)
    {
        _titleBarIsLight = isLight;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        int useDark = isLight ? 0 : 1;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref useDark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Rebinds every shortcut from the current config, dropping the ones bound before.</summary>
    private void ApplyKeyBindings()
    {
        foreach (var binding in _configuredBindings)
            InputBindings.Remove(binding);
        _configuredBindings.Clear();

        foreach (var binding in KeyBindingsRegistry.Build(_viewModel.Config.Keybindings, _viewModel.Actions))
        {
            InputBindings.Add(binding);
            _configuredBindings.Add(binding);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.Config)) ApplyKeyBindings();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        ApplyTitleBarTheme(_titleBarIsLight);
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
