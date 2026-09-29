using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ream.App.Animation;
using Ream.App.Controls;
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
    private readonly WheelAccumulator _zoomWheel = new();
    private readonly List<InputBinding> _configuredBindings = [];
    private readonly FullscreenController _fullscreen;
    private readonly IWindowBackdrop _backdrop;
    private WindowAppearance? _appearance;
    private readonly RibbonVisibility _ribbonState = new();
    private readonly DispatcherTimer _ribbonHideTimer = new() { Interval = RibbonHideDelay };
    private bool _overTabRow;
    private bool _overRibbonPanel;
    private bool _ribbonShown = true;
    private FindReplaceWindow? _findReplaceWindow;

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
        NavigationPane.DataContext = new NavigationPaneViewModel(viewModel);
        FileRibbon.HelpRequested += OpenHelp;
        FileRibbon.AboutRequested += OpenAbout;
        // Read Mode / Draft / Outline / Ruler / One Page are CheckBoxes/a ToggleButton bound straight to SettingsViewModel
        // passthrough properties now (FocusedNoteIsReadOnly etc., OnePageMode) - no event-relay needed.
        ViewRibbon.ThemeRequested += OpenThemeModal;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        SelectTab(RibbonTab.Home);

        _ribbonHideTimer.Tick += (_, _) => CompleteRibbonHide();
        Ribbon.MenuOpenChanged += UpdateMenuOpenState;
        ViewRibbon.MenuOpenChanged += UpdateMenuOpenState;
        ApplyRibbonMode(viewModel.Config.Ribbon.AutoHide);

        _fullscreen = new FullscreenController(new WindowFrame(this));
        viewModel.AppFullscreenToggleRequested += () =>
        {
            _fullscreen.Toggle();
            ApplyFullscreenChrome(_fullscreen.IsFullscreen);
        };
        Ribbon.PanModeToggleRequested += () => Settings.PanModeOn = !Settings.PanModeOn;

        ApplyKeyBindings();
        RefreshBoardZoomGestures();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.FindRequested += OnFindRequested;

        // The new note's view may not exist yet when focus is requested, so wait until layout has caught up.
        viewModel.FocusEditorRequested += () => Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => viewModel.CurrentWorkspace.FocusedNote?.RequestEditorFocus());
        Loaded += (_, _) => viewModel.RequestEditorFocus();
        // WorkspaceStripPanel's own clip already matches CanvasArea's Grid.Column="0" clip exactly at 100% zoom
        // with no pan, so turning it off here (once, unconditionally) rather than toggling it live has no visible
        // effect until Board Zoom is actually used - see the Board Zoom note further down for the rest of the story.
        Loaded += (_, _) =>
        {
            WorkspaceStrip.ClipToBounds = false;
            // WorkspaceStrip's own ActualHeight is just the current workspace's one band (ArrangeOverride positions
            // every child at that same size, however many bands it stacks beyond it) - the default RenderTransform
            // origin (0,0, the panel's own top-left) is that band's own TOP, so scaling down from there pulls
            // everything toward it: the current workspace shrinks toward its own top edge, workspaces above it
            // (arranged at negative Y - see ArrangeOverride) shrink away from view entirely rather than toward it,
            // and nothing above the current workspace can ever appear no matter how far zoomed out. Anchoring at
            // (0.5, 0.5) instead - the current workspace's own center - fixes both at once: that point stays fixed
            // on screen at any zoom (so the view expands outward from it symmetrically, not toward a corner), and
            // workspaces both above and below become reachable as more of the now-larger logical space comes into
            // the same screen area. TranslateTransform (the pan half of Board Zoom's own RenderTransform) is
            // unaffected either way - translation is origin-invariant, so drag-to-pan still moves by exactly the
            // screen pixels it always did.
            WorkspaceStrip.RenderTransformOrigin = new Point(0.5, 0.5);
        };
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
    private const int ZoomWheelStepPercent = 10;
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
        Settings.SetRibbonPinned(_ribbonState.Pinned);
        UpdateRibbon();
    }

    /// <summary>A click outside the ribbon puts it away, unless it is pinned or one of its menus is open. Also
    /// where a canvas click gets sorted into one of two unrelated things it might mean - see PanActive's own note.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && IsInsideCanvas(e.OriginalSource as DependencyObject))
        {
            if (PanActive)
            {
                _dragStart = e.GetPosition(CanvasArea);
                _dragged = false;
                CanvasArea.CaptureMouse();
            }
            else if (Settings.BoardZoomPercent < 100)
            {
                // Not a drag at all - a plain click while the board is pulled back enough that a neighboring
                // workspace might be showing. DrillIntoBoardClick is a no-op if this one wasn't (it lands on the
                // current workspace's own band, or past the last real one), so nothing needs to check that first.
                DrillIntoBoardClick(e.GetPosition(WorkspaceStrip));
            }
        }

        if (_ribbonShown && _ribbonState.AutoHide && !_ribbonState.Pinned && !_ribbonState.MenuOpen
            && !IsInsideRibbon(e.OriginalSource as DependencyObject))
        {
            DismissRibbon();
        }

        base.OnPreviewMouseDown(e);
    }

    /// <summary>Drag-to-pan: once the pointer has moved a few pixels from where the button went down, every further
    /// move pans by exactly that many screen pixels (see UpdateBoardTransform on why that's true at any zoom
    /// level).</summary>
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_dragStart is { } start)
        {
            var position = e.GetPosition(CanvasArea);
            var moved = position - start;
            if (!_dragged && moved.Length > 4) _dragged = true;
            if (_dragged)
            {
                _boardPanX += moved.X;
                _boardPanY += moved.Y;
                _dragStart = position;
                UpdateBoardTransform();
            }
        }

        base.OnPreviewMouseMove(e);
    }

    /// <summary>Ends a drag started above - a plain click was already handled on the way down (OnPreviewMouseDown),
    /// not here, since a click that isn't the start of a drag never sets _dragStart at all.</summary>
    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (_dragStart is not null && e.ChangedButton == MouseButton.Left)
        {
            CanvasArea.ReleaseMouseCapture();
            _dragStart = null;
            _dragged = false;
        }

        base.OnPreviewMouseUp(e);
    }

    /// <summary>Escape puts an open ribbon away (and still does its usual job elsewhere). Also where holding the
    /// configured pan gesture is noticed - see PanActive's own note - but only for a gesture that does NOT include
    /// Alt; an Alt-chord (the default, Alt+X) is handled at the raw window-message level instead, in
    /// OnWindowMessage, for reasons explained there.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_panGesture is { } gesture && !gesture.Modifiers.HasFlag(ModifierKeys.Alt)
            && e.Key == gesture.Key && Keyboard.Modifiers == gesture.Modifiers && !_panKeyHeld)
        {
            _panKeyHeld = true;
            UpdatePanCursor();
        }

        if (e.Key == Key.Escape && _ribbonShown && _ribbonState.AutoHide && !_ribbonState.Pinned && !_ribbonState.MenuOpen)
            DismissRibbon();

        base.OnPreviewKeyDown(e);
    }

    /// <summary>Releasing either half of the pan gesture's chord ends the hold - checking modifiers, not just the
    /// specific key event.Key names, is what catches "let go of Alt but kept Space down" too (Keyboard.Modifiers
    /// already reflects the key this same event is releasing by the time PreviewKeyUp runs). Not for an Alt-chord -
    /// see OnPreviewKeyDown's own note.</summary>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (_panKeyHeld && _panGesture is { Modifiers: var mods } gesture && !mods.HasFlag(ModifierKeys.Alt)
            && (e.Key == gesture.Key || Keyboard.Modifiers != gesture.Modifiers))
        {
            _panKeyHeld = false;
            UpdatePanCursor();
        }

        base.OnPreviewKeyUp(e);
    }

    /// <summary>The window losing focus (Alt-Tab, clicking another app) can't be trusted to deliver a matching key-up
    /// for whatever was held - without this, a hand cursor could get stuck showing forever.</summary>
    protected override void OnDeactivated(EventArgs e)
    {
        _panKeyHeld = false;
        UpdatePanCursor();
        base.OnDeactivated(e);
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

    /// <summary>Puts the panel away until it is wanted (auto-hide on) or leaves it up for good (off). Also restores
    /// the pin from config (startup, and any later reload) - AutoHide is set first, since a restored pin is no more
    /// meaningful than a clicked one with auto-hide off.</summary>
    internal void ApplyRibbonMode(bool autoHide)
    {
        _ribbonState.AutoHide = autoHide;
        _ribbonState.RestorePinned(_viewModel.Config.Ribbon.Pinned);
        PinButton.IsChecked = _ribbonState.Pinned;
        PinButton.Visibility = autoHide ? Visibility.Visible : Visibility.Collapsed;

        _ribbonHideTimer.Stop();
        ShowRibbon(_ribbonState.WantsOpen, animate: false);
    }

    /// <summary>Either ribbon's drop-down/menu (Home's font & size boxes and color menus; View's Switch Notes / Switch
    /// Workspaces menus) keeps the panel up regardless of the pin, so a menu never outlives the ribbon it opened
    /// from - previously only Home's own menus were watched, so an unpinned ribbon could vanish out from under an
    /// open View-tab menu the moment the pointer left it.</summary>
    private void UpdateMenuOpenState()
    {
        _ribbonState.MenuOpen = Ribbon.IsMenuOpen || ViewRibbon.IsMenuOpen;
        UpdateRibbon();
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

    /// <summary>The View tab's Theme button: Layout, Theme and Opacity in one modal, replacing the three separate ribbon groups they used to be.</summary>
    internal void OpenThemeModal() => Present(new ThemeModal(Settings));

    /// <summary>Opens Find (or Find and Replace) on whichever editor last had focus; modeless, so it re-shows an already-open window rather than stacking another.</summary>
    internal void OnFindRequested(bool withReplace)
    {
        if (Ribbon.CurrentEditor is not { } editor) return;

        if (_findReplaceWindow is { IsVisible: true } open)
        {
            open.SetMode(withReplace);
            open.Activate();
            return;
        }

        _findReplaceWindow = new FindReplaceWindow(editor, withReplace) { Owner = this };
        _findReplaceWindow.Closed += (_, _) => _findReplaceWindow = null;
        _findReplaceWindow.Show();
    }

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
        RefreshBoardZoomGestures();
        if (_viewModel.Config.Ribbon.AutoHide != _ribbonState.AutoHide) ApplyRibbonMode(_viewModel.Config.Ribbon.AutoHide);
    }

    private static readonly GridLength NavigationPaneWidth = new(280);

    /// <summary>Show group's Gridlines and Navigation Pane, Zoom's own Board Zoom, and Home's Pan toggle: all
    /// session-only view state (SettingsViewModel), not config, so the window just reacts to them directly rather
    /// than through Config.</summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.BoardZoomPercent):
                ApplyBoardZoom();
                return;
            case nameof(SettingsViewModel.PanModeOn):
                Ribbon.PanModeButton.IsChecked = Settings.PanModeOn;
                UpdatePanCursor();
                return;
        }

        if (e.PropertyName != nameof(SettingsViewModel.NavigationPaneOpen)) return;

        bool open = Settings.NavigationPaneOpen;
        NavigationPaneColumn.Width = open ? NavigationPaneWidth : new GridLength(0);
        NavigationPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open && NavigationPane.DataContext is NavigationPaneViewModel pane) pane.Refresh();
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

    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // WPF has no event for a horizontal (tilt) wheel, so read the raw message. Also where an Alt-chord pan gesture
    // (the default, Alt+X) is caught - see the block below for why it can't be done the normal way. Internal
    // (not private), like DrillIntoBoardClick, so a test can drive it directly: it's real window messages, not
    // anything WPF's own synthetic KeyEventArgs can carry (there is no HwndSource, and thus no raw message pump,
    // for an off-screen test window the way a real one gets).
    internal IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (MouseTilt.TryGetDelta(message, wParam.ToInt64(), out int delta))
        {
            _viewModel.FocusNoteBy(_tiltWheel.Add(delta));
            handled = true;
            return IntPtr.Zero;
        }

        // An Alt+<key> combination is a "system" key as far as Win32 is concerned (WM_SYSKEYDOWN/UP, not plain
        // WM_KEYDOWN/UP) - two consequences, both only for an Alt-chord: WPF's own routed KeyDown/Up reports
        // Key.System with the real key in SystemKey instead of Key directly (which is why OnPreviewKeyDown/Up
        // above only handle a non-Alt gesture - checking e.Key against a Space-with-Alt gesture there would never
        // match); and, for Space specifically, an unhandled WM_SYSKEYDOWN reaches DefWindowProc, which turns it
        // into WM_SYSCOMMAND/SC_KEYMENU - the window's own system menu - before WPF's routed event ever fires, and
        // marking that routed event handled afterward doesn't reach back and stop it. Reading the raw message
        // (with KeyInterop translating the configured Key to the matching virtual-key code, so this still honors
        // whatever panCanvas is actually configured to, not just the Alt+X default) is the only place both the
        // hold and the suppression can happen together.
        if (_panGesture is { } gesture && gesture.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            int vk = KeyInterop.VirtualKeyFromKey(gesture.Key);
            if (message == WM_SYSKEYDOWN && wParam.ToInt32() == vk && Keyboard.Modifiers == gesture.Modifiers)
            {
                if (!_panKeyHeld) { _panKeyHeld = true; UpdatePanCursor(); }
                handled = true;
            }
            // Either half of the chord letting go ends the hold - releasing the configured key itself, or Alt
            // (which also arrives as a WM_SYSKEYUP, just for a different virtual-key than the gesture's own).
            else if (_panKeyHeld && message == WM_SYSKEYUP && (wParam.ToInt32() == vk || !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
            {
                _panKeyHeld = false;
                UpdatePanCursor();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    // Plain wheel is deliberately left alone so it scrolls the note under the cursor.
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (_boardZoomWheelModifiers != ModifierKeys.None && modifiers == _boardZoomWheelModifiers)
        {
            // Checked first, before the broader single-modifier cases below: the default (Ctrl+Alt) would otherwise
            // match "HasFlag(Alt)" and switch workspaces instead.
            int steps = _boardZoomWheel.Add(e.Delta);
            if (steps != 0) Settings.BoardZoomPercent += steps * ZoomWheelStepPercent;
            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            _viewModel.SwitchWorkspace(-_workspaceWheel.Add(e.Delta));
            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewModel.FocusNoteBy(-_rowWheel.Add(e.Delta));
            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            int steps = _zoomWheel.Add(e.Delta);
            if (steps != 0) Settings.ZoomPercent += steps * ZoomWheelStepPercent;
            e.Handled = true;
        }

        base.OnPreviewMouseWheel(e);
    }

    // ===== Board Zoom: a live 0-100% dial (Zoom group's own dropdown, 100 = normal), not a toggled mode =====
    //
    // The idea: WorkspaceStripPanel already arranges every workspace, one full-viewport-height band per index,
    // stacked vertically (ArrangeOverride: child i at Y = (i - ScrollOffset) * viewportHeight) - it just also clips
    // itself to exactly one band's worth, so only the current workspace is ever visible. Turning that clip off
    // (once, in the constructor - see its own comment there; there's no "on" to turn it off for any more) and
    // applying a RenderTransform (scale, then pan, in that order, so panning always moves by screen pixels
    // regardless of zoom level) to the panel itself reveals the neighbors for free, correctly scaled, with no
    // change to the panel's own layout math at all. CanvasArea's own Grid.Column="0" picks up the clip instead, so
    // the zoomed-out view stays inside the canvas rather than spilling into the navigation pane or the window
    // chrome. WorkspaceStripPanel.NearRadius grows in proportion to how far zoomed out the board is (ApplyBoardZoom,
    // below), so whatever the zoom reveals actually has its notes loaded rather than showing as empty cards - that
    // radius otherwise only covers about a screen and a half either side of the current workspace, plenty at 100%
    // but nowhere near enough once several more workspaces are on screen at once.
    //
    // Panning has two independent sources - config's own held gesture ("panCanvas", default Alt+X) and the Home
    // tab's own Pan toggle (a sticky version of the same thing) - see PanActive's own note. One known rough edge,
    // left as-is for now: zoom always centers on the panel's own origin rather than the cursor, so it doesn't zoom
    // "into" whatever you're pointing at.

    private const double BoardZoomMinScale = 0.01; // the ribbon dropdown allows 0%, but NearRadius's own math below
                                                     // divides by the scale - this is the practical floor the actual
                                                     // render transform uses, however low the shown value goes.

    private readonly WheelAccumulator _boardZoomWheel = new();
    private ModifierKeys _boardZoomWheelModifiers;
    private KeyGesture? _panGesture;
    private bool _panKeyHeld;
    private double _boardPanX;
    private double _boardPanY;
    private Point? _dragStart;
    private bool _dragged;
    private WorkspaceStripPanel? _workspaceStrip;

    private WorkspaceStripPanel WorkspaceStrip => _workspaceStrip ??= FindVisualChild<WorkspaceStripPanel>(WorkspaceStripHost)
        ?? throw new InvalidOperationException("WorkspaceStripHost has no WorkspaceStripPanel yet.");

    /// <summary>Panning is active from either of two independent sources - config's own held gesture (_panKeyHeld,
    /// momentary: down for as long as the chord is held) or the Home tab's own Pan toggle (Settings.PanModeOn,
    /// sticky: on until clicked again) - and what a canvas click/drag means depends on whether either is true right
    /// now, so every place that needs to know checks this rather than the two sources separately.</summary>
    private bool PanActive => _panKeyHeld || Settings.PanModeOn;

    private void UpdatePanCursor() => CanvasArea.Cursor = PanActive ? Cursors.Hand : null;

    /// <summary>Exposed for tests only, to check config's two special gestures parsed the way they were supposed to.</summary>
    internal ModifierKeys BoardZoomWheelModifiers => _boardZoomWheelModifiers;
    internal KeyGesture? PanGesture => _panGesture;

    private void RefreshBoardZoomGestures()
    {
        _boardZoomWheelModifiers = ParseModifiers(_viewModel.Config.Keybindings.GetValueOrDefault("boardZoomWheel"));
        _panGesture = ParseKeyGesture(_viewModel.Config.Keybindings.GetValueOrDefault("panCanvas"));
    }

    /// <summary>Modifiers-only gestures (config's own "boardZoomWheel" - there's no key to hold down for a wheel
    /// notch, only modifiers) aren't something KeyGestureConverter parses (it always wants a real key), so this
    /// reads them by hand: the same "+"-separated shape as every other gesture in config.json, minus the key.
    /// Never throws - an unrecognized token just isn't a modifier, the same "hand-edited config, worst case it
    /// doesn't bind" philosophy KeyBindingsRegistry already uses for real key gestures.</summary>
    private static ModifierKeys ParseModifiers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ModifierKeys.None;

        var modifiers = ModifierKeys.None;
        foreach (var token in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            modifiers |= token.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None,
            };
        }
        return modifiers;
    }

    /// <summary>config's own "panCanvas" is an ordinary key gesture (Alt+X, by default) despite meaning
    /// something different from every other one (a hold, not a press) - so it parses exactly the way
    /// KeyBindingsRegistry parses every other action's gesture, just never becomes a KeyBinding at all (there's no
    /// single command a hold could Execute()).</summary>
    private static KeyGesture? ParseKeyGesture(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return new KeyGestureConverter().ConvertFromString(text) as KeyGesture;
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private const double RestingNearRadius = 1.5; // WorkspaceStripPanel's own default - unchanged at rest
    private const double RestingNoteMargin = 1.0; // NoteRowPanel's own default - unchanged at rest
    private const double ExploringNearRadius = 1000; // "basically everything" - see the note on NearMargin below
    private const double ExploringNoteMargin = 1000;

    private void ApplyBoardZoom() => UpdateBoardTransform();

    /// <summary>Exposed for tests only, to simulate "the user has panned" without a real, position-controlled drag
    /// (see this file's own note on why drag-to-pan itself has no direct test).</summary>
    internal void SetBoardPanForTests(double x, double y)
    {
        _boardPanX = x;
        _boardPanY = y;
        UpdateBoardTransform();
    }

    /// <summary>
    /// Called for every zoom or pan change alike (a live property change, or every pointer move during a drag), so
    /// both the visual transform and how generously content around it loads stay current together.
    /// </summary>
    private void UpdateBoardTransform()
    {
        double scale = Math.Max(Settings.BoardZoomPercent / 100.0, BoardZoomMinScale);
        // Scale first, then translate, so a drag always pans by the same number of screen pixels no matter how
        // far zoomed out you are - TransformGroup composes its children in list order, innermost first.
        WorkspaceStrip.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(scale, scale), new TranslateTransform(_boardPanX, _boardPanY) },
        };

        // Not "at rest" (100%, no pan) - the board is being actively explored, so load generously rather than try
        // to compute exactly how far zoom and an arbitrary, unbounded pan offset together put things off-screen.
        // Both radii are counted in their own panel's units (workspaces for one, viewport-widths for the other),
        // not pixels, so 1000 of either is far more than any real ream has to actually load - the loop in each
        // panel's own ArrangeOverride is still bounded by how many children it actually has.
        bool exploring = Settings.BoardZoomPercent != 100 || _boardPanX != 0 || _boardPanY != 0;
        WorkspaceStrip.NearRadius = exploring ? ExploringNearRadius : RestingNearRadius;
        NoteRowPanel.SetNearMargin(WorkspaceStrip, exploring ? ExploringNoteMargin : RestingNoteMargin);
    }

    private bool IsInsideCanvas(DependencyObject? source)
    {
        for (var node = source; node is not null;)
        {
            if (ReferenceEquals(node, CanvasArea)) return true;
            node = (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>Which workspace a click at this position (already in WorkspaceStrip's own local coordinates - WPF's
    /// own GetPosition already accounts for its RenderTransform, so this needs no scale/pan math of its own) landed
    /// on, mirroring WorkspaceStripPanel.ArrangeOverride's own placement (child i at (i - CurrentIndex) * viewport
    /// height - using CurrentIndex rather than the panel's own live ScrollOffset, since a click only makes sense
    /// once any switch animation has long since settled). A no-op if the click landed on the current workspace's
    /// own band (nothing to drill into - and OnPreviewMouseDown calls this for every plain click while zoomed out
    /// at all, including ordinary clicks into the current note, which must keep working normally) or past the last
    /// real workspace. Internal (not private) so tests can drive it directly: a synthetic MouseButtonEventArgs
    /// carries no position a test can control (GetPosition reads the shared MouseDevice's own last-known position,
    /// not anything on the event args), so the real down/move/up handlers above aren't reachable the way a Click
    /// event is - this is the one piece of that pipeline worth testing on its own.</summary>
    internal void DrillIntoBoardClick(Point localPosition)
    {
        double viewportHeight = WorkspaceStrip.ActualHeight;
        if (viewportHeight <= 0) return;

        int index = _viewModel.CurrentIndex + (int)Math.Floor(localPosition.Y / viewportHeight);
        if (index < 0 || index >= _viewModel.Workspaces.Count || index == _viewModel.CurrentIndex) return;

        // "drill into it": land at the normal view of wherever you clicked, not just zoomed back to 100% wherever
        // panning happened to leave the view - the whole point of drilling in is a clean, centered arrival.
        Settings.BoardZoomPercent = 100;
        _boardPanX = 0;
        _boardPanY = 0;
        UpdateBoardTransform(); // in case it was already 100 (this workspace only came into view via a pan, say),
                                 // which wouldn't otherwise raise the property-changed that normally calls this
        _viewModel.SelectWorkspaceCommand.Execute(_viewModel.Workspaces[index]);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
