using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Ream.Core.Layout;
using Ream.Core.Models;

namespace Ream.App.Views;

/// <summary>
/// Read-only list of every shortcut, as currently configured. It moves like the app does: the switch-workspace keys (Alt+Down and
/// Alt+Up unless rebound) step a highlight from one section to the next, and past the last section onto the Close button.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly HelpNavigator _navigator;

    /// <param name="keybindings">The configured gestures, so the navigation keys follow a rebind of switchWorkspaceUp/Down; null uses the defaults.</param>
    public HelpWindow(IReadOnlyList<HelpSection> sections, IReadOnlyDictionary<string, string>? keybindings = null)
    {
        InitializeComponent();
        Sections.ItemsSource = sections;
        _navigator = new HelpNavigator(sections.Count);

        keybindings ??= AppConfig.DefaultKeybindings();
        BindKey("switchWorkspaceDown", keybindings, NavigateDown);
        BindKey("switchWorkspaceUp", keybindings, NavigateUp);
        HintText.Text = $"{GestureText.Pretty(GestureOf("switchWorkspaceDown", keybindings))} and " +
                        $"{GestureText.Pretty(GestureOf("switchWorkspaceUp", keybindings))} move through the sections. Esc closes.";

        Loaded += (_, _) => ShowHighlight();
        SizeChanged += (_, _) => CenterHighlighted();
    }

    /// <summary>Which section is highlighted (null while the Close button is).</summary>
    internal int? HighlightedSection => _navigator.SectionIndex;

    internal bool CloseHighlighted => _navigator.OnClose;

    internal void NavigateDown()
    {
        if (_navigator.Down()) ShowHighlight();
    }

    internal void NavigateUp()
    {
        if (_navigator.Up()) ShowHighlight();
    }

    private void BindKey(string action, IReadOnlyDictionary<string, string> keybindings, Action run)
    {
        var converter = new KeyGestureConverter();
        var gesture = TryGesture(converter, GestureOf(action, keybindings))
            ?? TryGesture(converter, AppConfig.DefaultKeybindings()[action]);
        if (gesture is not null) InputBindings.Add(new KeyBinding(new RelayCommand(run), gesture));
    }

    private static string GestureOf(string action, IReadOnlyDictionary<string, string> keybindings) =>
        keybindings.TryGetValue(action, out var gesture) && !string.IsNullOrWhiteSpace(gesture)
            ? gesture
            : AppConfig.DefaultKeybindings()[action];

    private static KeyGesture? TryGesture(KeyGestureConverter converter, string text)
    {
        try
        {
            return converter.ConvertFromString(text) as KeyGesture;
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    // ----- Showing where you are -----

    private void ShowHighlight()
    {
        for (int i = 0; i < _navigator.SectionCount; i++)
        {
            if (CardOf(i) is not { } card) continue;

            bool on = _navigator.SectionIndex == i;
            card.SetResourceReference(Border.BorderBrushProperty, on ? "FocusBorderBrush" : "CardBorderBrush");
            card.BorderThickness = new Thickness(on ? 2 : 1);
        }

        if (_navigator.OnClose)
        {
            CloseButton.SetResourceReference(Control.BorderBrushProperty, "FocusBorderBrush");
            CloseButton.BorderThickness = new Thickness(2);
            CloseButton.Focus();
        }
        else
        {
            CloseButton.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
            CloseButton.BorderThickness = new Thickness(1);
            Keyboard.ClearFocus();
        }

        CenterHighlighted();
    }

    private Border? CardOf(int index)
    {
        if (Sections.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter presenter) return null;

        presenter.ApplyTemplate();
        return presenter.ContentTemplate?.FindName("Card", presenter) as Border;
    }

    /// <summary>Scrolls the highlighted section to the middle of the list, like the row of notes centers the focused note.</summary>
    private void CenterHighlighted()
    {
        if (_navigator.SectionIndex is not { } index) return;
        if (Sections.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container) return;

        UpdateLayout();
        double topInView = container.TranslatePoint(new Point(0, 0), Scroll).Y;
        double target = Scroll.VerticalOffset + topInView - (Scroll.ViewportHeight - container.ActualHeight) / 2;
        Scroll.ScrollToVerticalOffset(Math.Clamp(target, 0, Math.Max(0, Scroll.ScrollableHeight)));
    }
}
