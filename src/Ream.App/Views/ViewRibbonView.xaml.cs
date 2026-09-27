using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.ViewModels;

namespace Ream.App.Views;

/// <summary>The View tab of the ribbon: laid out like Word's own, plus Ream's own settings tacked on at the end. Its DataContext is the <c>SettingsViewModel</c>.</summary>
public partial class ViewRibbonView : UserControl
{
    private const int ZoomStep = 10;
    private bool _menuOpen;
    private Popup? _switchWorkspacesMenu;
    private Popup? _switchNotesMenu;

    public ViewRibbonView()
    {
        InitializeComponent();
        // Owning "click elsewhere closes it" ourselves (see OpenMenu) needs a window to watch clicks on.
        Loaded += (_, _) => { if (Window.GetWindow(this) is { } window) window.PreviewMouseDown += OnWindowPreviewMouseDown; };
        Unloaded += (_, _) => { if (Window.GetWindow(this) is { } window) window.PreviewMouseDown -= OnWindowPreviewMouseDown; };
    }

    /// <summary>The Theme tile: opening a window (ownership, the test-interceptable ShowModal hook) is MainWindow's job, not this tab's - same reason Help/About are raised as events too.</summary>
    public event Action? ThemeRequested;

    /// <summary>Raised when the Switch Notes or Switch Workspaces menu opens or closes.</summary>
    public event Action? MenuOpenChanged;

    /// <summary>A Switch Notes / Switch Workspaces menu is open. The window keeps an auto-hidden ribbon up meanwhile - same reason RibbonView tracks its own font/size drop-downs and color menus.</summary>
    internal bool IsMenuOpen => _menuOpen;

    /// <summary>Exposed for tests only, to reach into whichever menu is currently open.</summary>
    internal Popup? SwitchWorkspacesMenu => _switchWorkspacesMenu;
    internal Popup? SwitchNotesMenu => _switchNotesMenu;

    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    private void OnThemeClick(object sender, RoutedEventArgs e) => ThemeRequested?.Invoke();

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

    /// <summary>Switch Workspaces: a themed drop-down of every named or occupied workspace, like the File ribbon's own Recent list but for jumping instead of opening.</summary>
    private void OnSwitchWorkspacesClick(object sender, RoutedEventArgs e)
    {
        if (CloseIfOpen(ref _switchWorkspacesMenu)) return;
        if (Settings is not { } settings) return;

        var panel = new StackPanel();
        var popup = BuildPopupShell(SwitchWorkspacesButton, panel);
        foreach (var workspace in settings.App.Workspaces)
        {
            if (workspace.Name is null && workspace.IsEmpty) continue; // the always-empty edges have nothing to switch to

            var target = workspace;
            panel.Children.Add(BuildRow(target.DisplayName ?? "New workspace", target.IsCurrent, () =>
            {
                popup.IsOpen = false;
                settings.App.SelectWorkspaceCommand.Execute(target);
            }));
        }
        popup.IsOpen = true;
        _switchWorkspacesMenu = popup;
    }

    /// <summary>Switch Notes: a themed drop-down of the current workspace's notes, like Word's own Switch Windows reduced to one window's worth of documents.</summary>
    private void OnSwitchNotesClick(object sender, RoutedEventArgs e)
    {
        if (CloseIfOpen(ref _switchNotesMenu)) return;
        if (Settings is not { } settings) return;

        var workspace = settings.App.CurrentWorkspace;
        var panel = new StackPanel();
        var popup = BuildPopupShell(SwitchNotesButton, panel);
        foreach (var note in workspace.Notes)
        {
            var target = note;
            panel.Children.Add(BuildRow(target.DisplayTitle, target.IsFocused, () =>
            {
                popup.IsOpen = false;
                workspace.SetFocus(workspace.Notes.IndexOf(target));
                settings.App.RequestEditorFocus();
            }));
        }
        popup.IsOpen = true;
        _switchNotesMenu = popup;
    }

    /// <summary>True (and closes it) if <paramref name="tracked"/> is still open from an earlier click on the same
    /// button - the "click it again to close it" half of the toggle. See <see cref="BuildPopupShell"/> for why this
    /// can safely just ask the tracked reference, unlike the ContextMenu this used to be built from.</summary>
    private static bool CloseIfOpen(ref Popup? tracked)
    {
        if (tracked is not { IsOpen: true }) { tracked = null; return false; }
        tracked.IsOpen = false;
        tracked = null;
        return true;
    }

    /// <summary>
    /// Three earlier attempts at "click the button again to close its own menu, not reopen it" all built on
    /// <c>ContextMenu</c>, each trying a different way to detect "this click's Click event is the one that just
    /// closed the menu" - checking <c>IsOpen</c> from inside <c>Click</c>, watching <c>IsOpen</c> change, watching
    /// <c>Closed</c>, and finally setting <c>ContextMenu.StaysOpen="True"</c> to turn off its click-outside dismissal
    /// entirely and own the whole lifecycle by hand. All four passed a synthetic test built around whatever signal
    /// they used and all four still failed in the live app: <c>ContextMenu</c>/<c>MenuBase</c> carries its own
    /// keyboard-navigation focus-scope machinery for arrow-key item navigation, access keys and submenus, and (near
    /// as this could be pinned down without being able to drive the real desktop to confirm it directly - see the
    /// root CLAUDE.md) that machinery closes the menu when focus moves elsewhere independently of <c>StaysOpen</c>,
    /// which only documents itself as governing the click-outside case. Clicking the anchor button again shifts
    /// keyboard focus to it, which was enough to trigger that separate close every time, no matter which signal was
    /// being watched for it. So this stops using <c>ContextMenu</c>/<c>MenuItem</c> altogether: a plain <c>Popup</c>
    /// is a framework primitive with none of that menu-specific baggage - it closes only when told to - so a
    /// hand-built one (this <c>Border</c>/<c>StackPanel</c>/<c>Border</c>-per-row shell, styled to match the
    /// ContextMenu/MenuItem template in Controls.xaml) genuinely can't be closed by anything but this file's own
    /// code: <see cref="CloseIfOpen"/> (same button clicked again) and <see cref="OnWindowPreviewMouseDown"/>
    /// (clicked elsewhere) below, plus each row's own click closing it after picking something. That makes the
    /// tracked field's own <c>IsOpen</c> an unconditionally accurate answer to "is this still up" - there's no
    /// competing mechanism left to race against.
    /// </summary>
    private Popup BuildPopupShell(Button anchor, Panel content)
    {
        var border = new Border
        {
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinWidth = 170,
            Child = content,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true,
            Child = border,
        };
        popup.Opened += (_, _) => SetMenuOpen(true);
        popup.Closed += (_, _) => SetMenuOpen(false);
        return popup;
    }

    /// <summary>One row of a switch drop-down: a checkmark (if this is the current workspace/note) and a label,
    /// laid out the same way Controls.xaml's own MenuItem template does it, since this replaces MenuItem here.</summary>
    private static Border BuildRow(string text, bool isCurrent, Action onClick)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (isCurrent)
        {
            var check = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            check.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            grid.Children.Add(check);
        }

        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var row = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brushes.Transparent, // must be non-null, not just unset, or the row won't hit-test as a whole
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.SetResourceReference(TextElement.ForegroundProperty, "ControlTextBrush");
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.PreviewMouseLeftButtonUp += (_, _) => onClick();
        return row;
    }

    /// <summary>
    /// The other half of owning the menu lifecycle: with <c>StaysOpen="True"</c>, a Popup won't close on its own
    /// when the user clicks elsewhere, so this does it by hand. A click actually inside an open popup never reaches
    /// here - its content is hosted in its own top-level window, not this one - so the only thing to rule out is a
    /// click on the very button that opened it (that button's own Click handler, above, already closes it; closing
    /// it here too would just reopen it a moment later, the exact bug this whole design avoids).
    /// </summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_switchWorkspacesMenu is { IsOpen: true } && !IsDescendantOf(e.OriginalSource, SwitchWorkspacesButton))
        {
            _switchWorkspacesMenu.IsOpen = false;
            _switchWorkspacesMenu = null;
        }
        if (_switchNotesMenu is { IsOpen: true } && !IsDescendantOf(e.OriginalSource, SwitchNotesButton))
        {
            _switchNotesMenu.IsOpen = false;
            _switchNotesMenu = null;
        }
    }

    private static bool IsDescendantOf(object originalSource, DependencyObject ancestor)
    {
        for (var node = originalSource as DependencyObject; node is not null; node = GetParent(node))
        {
            if (ReferenceEquals(node, ancestor)) return true;
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node) =>
        (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);

    private void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        MenuOpenChanged?.Invoke();
    }
}
