using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ream.App.Animation;
using Ream.App.Input;
using Ream.App.Services;
using Ream.App.Views;
using Ream.Core.Layout;
using Ream.Core.Models;
using Ream.App.ViewModels;
using Ream.Core.Utilities;

namespace Ream.App;

/// <summary>The tabs above the ribbon panel.</summary>
internal enum RibbonTab { File, Home, View }

public partial class MainWindow : Window
{
    private readonly AppViewModel _viewModel;
    private readonly WheelAccumulator _workspaceWheel = new();
    private readonly WheelAccumulator _rowWheel = new();
    private readonly WheelAccumulator _tiltWheel = new();
    private readonly List<InputBinding> _configuredBindings = [];
    private readonly FullscreenController _fullscreen;
    private readonly IWindowBackdrop _backdrop;
    private WindowAppearance? _appearance;
    private readonly RibbonVisibility _ribbonState = new();
    private readonly DispatcherTimer _ribbonHideTimer = new() { Interval = RibbonHideDelay };
    private bool _overTabRow;
    private bool _overRibbonPanel;
    private bool _ribbonShown = true;

    /// <param name="settings">What the View tab edits; when omitted the panel works on this run only (tests).</param>
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
        ViewRibbon.DataContext = Settings;
        FileRibbon.HelpRequested += OpenHelp;
        FileRibbon.AboutRequested += OpenAbout;
        SelectTab(RibbonTab.Home);

        _ribbonHideTimer.Tick += (_, _) => CompleteRibbonHide();
        Ribbon.MenuOpenChanged += () =>
        {
            _ribbonState.MenuOpen = Ribbon.IsMenuOpen;
            UpdateRibbon();
        };
        ApplyRibbonMode(viewModel.Config.Ribbon.AutoHide);

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
        TitleBar.Tag = fullscreen ? "fullscreen" : null;

    // ----- Renaming the workspace from the corner label -----

    private void OnWorkspaceLabelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;

        _viewModel.BeginRenameCommand.Execute(null);
        e.Handled = true;
    }

    private void OnWorkspaceNameKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.CommitRenameCommand.Execute(_viewModel.CurrentWorkspace);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.CancelRenameCommand.Execute(_viewModel.CurrentWorkspace);
                e.Handled = true;
                break;
        }
    }

    // Clicking away keeps what was typed. Escape has already ended the rename, so this then does nothing.
    private void OnWorkspaceNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _viewModel.CurrentWorkspace.CommitRename();

    private void OnWorkspaceNameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not System.Windows.Controls.TextBox box) return;

        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    // ----- The ribbon tabs -----

    /// <summary>Which tab's ribbon is showing.</summary>
    internal RibbonTab SelectedTab { get; private set; }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        var tab = Enum.Parse<RibbonTab>((string)((FrameworkElement)sender).Tag);
        bool wasSelected = tab == SelectedTab;

        SelectTab(tab);
        _ribbonState.TabClicked(wasSelected);
        UpdateRibbon();
    }

    // ----- Auto-hide -----

    /// <summary>How long the panel waits after the pointer leaves before tucking away.</summary>
    internal static readonly TimeSpan RibbonHideDelay = TimeSpan.FromMilliseconds(400);

    private const int RibbonSlideMs = 140;
    private const double RibbonHeight = 90;

    /// <summary>What decides whether the panel is up (tests read it; the window feeds it).</summary>
    internal RibbonVisibility RibbonState => _ribbonState;

    /// <summary>The panel is up (or sliding up); false once it is tucked away.</summary>
    internal bool IsRibbonOpen => _ribbonShown;

    /// <summary>The pointer left and the panel is about to tuck away.</summary>
    internal bool RibbonHidePending => _ribbonHideTimer.IsEnabled;

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _ribbonState.TogglePin();
        PinButton.IsChecked = _ribbonState.Pinned;
        UpdateRibbon();
    }

    /// <summary>A click outside the ribbon puts it away, unless it is pinned or one of its menus is open.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (_ribbonShown && _ribbonState.AutoHide && !_ribbonState.Pinned && !_ribbonState.MenuOpen
            && !IsInsideRibbon(e.OriginalSource as DependencyObject))
        {
            DismissRibbon();
        }

        base.OnPreviewMouseDown(e);
    }

    /// <summary>Escape puts an open ribbon away (and still does its usual job elsewhere).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _ribbonShown && _ribbonState.AutoHide && !_ribbonState.Pinned && !_ribbonState.MenuOpen)
            DismissRibbon();

        base.OnPreviewKeyDown(e);
    }

    private bool IsInsideRibbon(DependencyObject? source)
    {
        for (var node = source; node is not null;)
        {
            if (ReferenceEquals(node, TabRow) || ReferenceEquals(node, RibbonPanel)) return true;

            // A control that has never been laid out has no visual parent yet, but it does have a logical one.
            node = (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>Forgets the tab click and the pointer, and tucks the panel away at once (no delay).</summary>
    internal void DismissRibbon()
    {
        _ribbonState.Dismiss();
        _overTabRow = false;
        _overRibbonPanel = false;
        CompleteRibbonHide();
    }

    private void OnRibbonAreaMouse(object sender, MouseEventArgs e)
    {
        bool inside = e.RoutedEvent == Mouse.MouseEnterEvent;
        if (ReferenceEquals(sender, TabRow)) _overTabRow = inside;
        else _overRibbonPanel = inside;

        _ribbonState.PointerInside = _overTabRow || _overRibbonPanel;
        UpdateRibbon();
    }

    /// <summary>Puts the panel away until it is wanted (auto-hide on) or leaves it up for good (off).</summary>
    internal void ApplyRibbonMode(bool autoHide)
    {
        _ribbonState.AutoHide = autoHide;
        PinButton.IsChecked = _ribbonState.Pinned;
        PinButton.Visibility = autoHide ? Visibility.Visible : Visibility.Collapsed;

        _ribbonHideTimer.Stop();
        ShowRibbon(_ribbonState.WantsOpen, animate: false);
    }

    /// <summary>Brings the panel up at once if it should be, or starts the countdown to tucking it away.</summary>
    internal void UpdateRibbon()
    {
        if (_ribbonState.WantsOpen)
        {
            _ribbonHideTimer.Stop();
            if (!_ribbonShown) ShowRibbon(true, animate: true);
        }
        else if (_ribbonShown && !_ribbonHideTimer.IsEnabled)
        {
            _ribbonHideTimer.Start();
        }
    }

    /// <summary>The delay is over: tuck the panel away unless something wants it again.</summary>
    internal void CompleteRibbonHide()
    {
        _ribbonHideTimer.Stop();
        if (!_ribbonState.WantsOpen && _ribbonShown) ShowRibbon(false, animate: true);
    }

    /// <summary>Grows the panel to its height, or shrinks it to nothing; the notes below move with it.</summary>
    private void ShowRibbon(bool open, bool animate)
    {
        _ribbonShown = open;
        double to = open ? RibbonHeight : 0;

        if (open) RibbonPanel.Visibility = Visibility.Visible;

        var animations = _viewModel.Config.Animations;
        if (!animate || !animations.Enabled)
        {
            RibbonPanel.BeginAnimation(HeightProperty, null);
            RibbonPanel.Height = to;
            RibbonPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var slide = new DoubleAnimation(to, TimeSpan.FromMilliseconds(RibbonSlideMs))
        {
            EasingFunction = Motion.CreateEasing(animations),
        };
        if (!open)
        {
            slide.Completed += (_, _) =>
            {
                if (!_ribbonShown) RibbonPanel.Visibility = Visibility.Collapsed;
            };
        }
        RibbonPanel.BeginAnimation(HeightProperty, slide);
    }

    internal void SelectTab(RibbonTab tab)
    {
        SelectedTab = tab;

        FileRibbon.Visibility = tab == RibbonTab.File ? Visibility.Visible : Visibility.Collapsed;
        Ribbon.Visibility = tab == RibbonTab.Home ? Visibility.Visible : Visibility.Collapsed;
        ViewRibbon.Visibility = tab == RibbonTab.View ? Visibility.Visible : Visibility.Collapsed;

        FileTab.IsChecked = tab == RibbonTab.File;
        HomeTab.IsChecked = tab == RibbonTab.Home;
        ViewTab.IsChecked = tab == RibbonTab.View;
        RibbonScroll.ScrollToHorizontalOffset(0);
    }

    private void OnRibbonScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RibbonScrollLeft.Visibility = RibbonScroll.HorizontalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        RibbonScrollRight.Visibility = RibbonScroll.HorizontalOffset < RibbonScroll.ScrollableWidth - 0.5
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnRibbonChevron(object sender, RoutedEventArgs e)
    {
        int direction = int.Parse((string)((FrameworkElement)sender).Tag);
        RibbonScroll.ScrollToHorizontalOffset(RibbonScroll.HorizontalOffset + direction * RibbonScroll.ViewportWidth * 0.75);
    }

    // The plain wheel over the ribbon scrolls it sideways (there is nothing to scroll vertically).
    private void OnRibbonWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || RibbonScroll.ScrollableWidth <= 0) return;

        RibbonScroll.ScrollToHorizontalOffset(RibbonScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>What the About window shows; the app fills in the real folders once it knows them.</summary>
    internal AboutInfo About { get; set; } = AboutInfo.Create();

    /// <summary>How Help and About are shown (modally, over this window). Tests replace it so nothing really opens.</summary>
    internal Action<Window> ShowModal { get; set; } = window => window.ShowDialog();

    internal void OpenHelp() => Present(new HelpWindow(HelpContent.Build(_viewModel.Config.Keybindings), _viewModel.Config.Keybindings));

    internal void OpenAbout() => Present(new AboutWindow(About with { DocumentsFolder = _viewModel.ReamPath ?? About.DocumentsFolder }));

    private void Present(Window window)
    {
        window.Owner = this;
        ShowModal(window);
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
        if (e.PropertyName != nameof(AppViewModel.Config)) return;

        ApplyKeyBindings();
        if (_viewModel.Config.Ribbon.AutoHide != _ribbonState.AutoHide) ApplyRibbonMode(_viewModel.Config.Ribbon.AutoHide);
    }

    /// <summary>Closing the window asks about unsaved changes (only ever when auto-save is off and something changed).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.Files is { } files && !files.ConfirmLeave()) e.Cancel = true;
        base.OnClosing(e);
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
