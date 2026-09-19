using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ream.App.Input;
using Ream.App.Services;
using Ream.App.Views;
using Ream.Core.Models;
using Ream.App.ViewModels;
using Ream.Core.Utilities;

namespace Ream.App;

public partial class MainWindow : Window
{
    private readonly AppViewModel _viewModel;
    private readonly WheelAccumulator _workspaceWheel = new();
    private readonly WheelAccumulator _rowWheel = new();
    private readonly WheelAccumulator _tiltWheel = new();
    private readonly List<InputBinding> _configuredBindings = [];
    private readonly FullscreenController _fullscreen;
    private DateTime _settingsClosedAt;
    private DateTime _fileMenuClosedAt;
    private readonly IWindowBackdrop _backdrop;
    private WindowAppearance? _appearance;

    /// <param name="settings">What the cogwheel panel edits; when omitted the panel works on this run only (tests).</param>
    internal MainWindow(AppViewModel viewModel, SettingsViewModel? settings, IWindowBackdrop? backdrop)
        : this(viewModel, settings)
    {
        _backdrop = backdrop ?? _backdrop;
    }

    public MainWindow(AppViewModel viewModel, SettingsViewModel? settings = null)
    {
        _backdrop = new WindowBackdrop(this);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Settings = settings ?? new SettingsViewModel(viewModel, new ThemeService(Application.Current));
        SettingsPanel.DataContext = Settings;

        _fullscreen = new FullscreenController(new WindowFrame(this));
        viewModel.AppFullscreenToggleRequested += () =>
        {
            _fullscreen.Toggle();
            ApplyFullscreenChrome(_fullscreen.IsFullscreen);
        };

        ApplyKeyBindings();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The new note's view may not exist yet when focus is requested, so wait until layout has caught up.
        viewModel.FocusEditorRequested += () => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => viewModel.CurrentWorkspace.FocusedNote?.RequestEditorFocus());
        Loaded += (_, _) => viewModel.RequestEditorFocus();
    }

    public SettingsViewModel Settings { get; }

    // ----- The window frame (drawn by Ream, so these do what the native buttons would) -----

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";

        // A maximized borderless window is laid out slightly bigger than the screen (by the resize border);
        // pull the content back in so nothing is cut off.
        WindowBorder.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    /// <summary>In app fullscreen the title bar goes away too (the rest of the top bar stays).</summary>
    internal void ApplyFullscreenChrome(bool fullscreen) =>
        TitleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>What the About window shows; the app fills in the real folders once it knows them.</summary>
    internal AboutInfo About { get; set; } = AboutInfo.Create();

    /// <summary>How Help and About are shown (modally, over this window). Tests replace it so nothing really opens.</summary>
    internal Action<Window> ShowModal { get; set; } = window => window.ShowDialog();

    private void OnFileClick(object sender, RoutedEventArgs e)
    {
        if (DateTime.UtcNow - _fileMenuClosedAt < TimeSpan.FromMilliseconds(250)) return;

        FileMenu.PlacementTarget = FileButton;
        FileMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        FileMenu.IsOpen = true;
    }

    private void OnFileMenuClosed(object sender, RoutedEventArgs e) => _fileMenuClosedAt = DateTime.UtcNow;

    private void OnHelpClick(object sender, RoutedEventArgs e) => OpenHelp();

    private void OnAboutClick(object sender, RoutedEventArgs e) => OpenAbout();

    internal void OpenHelp() => Present(new HelpWindow(HelpContent.Build(_viewModel.Config.Keybindings)));

    internal void OpenAbout() => Present(new AboutWindow(About));

    private void Present(Window window)
    {
        window.Owner = this;
        ShowModal(window);
        _viewModel.RequestEditorFocus();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        // Clicking the button while the panel is open first closes it (it lost focus); don't reopen it straight away.
        if (DateTime.UtcNow - _settingsClosedAt < TimeSpan.FromMilliseconds(250)) return;
        SettingsPopup.IsOpen = true;
    }

    private void OnSettingsClosed(object? sender, EventArgs e)
    {
        _settingsClosedAt = DateTime.UtcNow;
        _viewModel.RequestEditorFocus();
    }

    /// <summary>
    /// Makes the title bar, border and blur match the theme. Safe to call before the window has a handle - it
    /// is applied again as soon as it does.
    /// </summary>
    internal void ApplyAppearance(WindowAppearance appearance)
    {
        _appearance = appearance;
        _backdrop.Apply(appearance);
    }

    /// <summary>Keeps the window's chrome matching the theme from now on (and right now).</summary>
    internal void FollowTheme(ThemeService theme)
    {
        theme.Changed += () => ApplyAppearance(theme.Appearance);
        ApplyAppearance(theme.Appearance);
    }

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
        if (_appearance is { } appearance) _backdrop.Apply(appearance);
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
