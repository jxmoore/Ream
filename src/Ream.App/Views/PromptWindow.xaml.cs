using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ream.App.Views;

internal enum PromptIcon { Question, Warning, Error, Info }

/// <param name="Label">The button text; an underscore marks its access key ("_Save").</param>
/// <param name="Result">What the dialog answers when this button is chosen.</param>
/// <param name="IsDefault">Enter chooses it, and it wears the accent border. If none is marked, the first is the default.</param>
/// <param name="IsCancel">Esc, and the title bar's close button, choose it.</param>
internal sealed record PromptButton(string Label, object Result, bool IsDefault = false, bool IsCancel = false);

/// <summary>Ream's own question / message box: the drawn title bar and theme colors instead of the native MessageBox.</summary>
internal sealed partial class PromptWindow : Window
{
    private readonly List<(Button Button, PromptButton Spec)> _buttons = [];
    private readonly PromptButton? _cancel;
    private PromptButton? _chosen;

    public PromptWindow(string title, string message, PromptIcon icon, IReadOnlyList<PromptButton> buttons)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        GlyphText.Text = GlyphOf(icon);
        GlyphText.SetResourceReference(TextBlock.ForegroundProperty, icon == PromptIcon.Error ? "ErrorBrush" : "AccentBrush");

        var defaultSpec = buttons.FirstOrDefault(b => b.IsDefault) ?? buttons.FirstOrDefault();
        _cancel = buttons.FirstOrDefault(b => b.IsCancel);

        for (int i = 0; i < buttons.Count; i++)
        {
            var spec = buttons[i];
            bool isDefault = ReferenceEquals(spec, defaultSpec);
            var button = new Button
            {
                Name = $"PromptButton{i}",
                Content = spec.Label,
                MinWidth = 88,
                Padding = new Thickness(16, 5, 16, 5),
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = ReferenceEquals(spec, _cancel),
            };
            if (isDefault)
            {
                button.SetResourceReference(Control.BorderBrushProperty, "FocusBorderBrush");
                button.BorderThickness = new Thickness(2);
            }

            button.Click += (_, _) => Choose(spec);
            ButtonRow.Children.Add(button);
            _buttons.Add((button, spec));
        }

        PreviewKeyDown += OnPreviewKeyDown;
        var initial = _buttons.FirstOrDefault(b => ReferenceEquals(b.Spec, defaultSpec)).Button;
        if (initial is not null) FocusManager.SetFocusedElement(this, initial);
        Loaded += (_, _) => initial?.Focus();
    }

    /// <summary>The chosen button's Result; the cancel button's when closed with the x or Esc; null if closed with the x and there is no cancel button.</summary>
    public object? Result => (_chosen ?? _cancel)?.Result;

    /// <summary>Shows the dialog modally over <paramref name="owner"/> (centered on the screen if there is none) and returns <see cref="Result"/>.</summary>
    public static object? Ask(Window? owner, string title, string message, PromptIcon icon, IReadOnlyList<PromptButton> buttons)
    {
        var window = new PromptWindow(title, message, icon, buttons);
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        window.ShowDialog();
        return window.Result;
    }

    private static string GlyphOf(PromptIcon icon) => icon switch
    {
        PromptIcon.Warning => "",
        PromptIcon.Error => "",
        PromptIcon.Info => "",
        _ => "",
    };

    // Closing is deferred: the button that was pressed may still be finishing its own click when it gets here.
    private void Choose(PromptButton spec)
    {
        if (_chosen is not null) return;

        _chosen = spec;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || Keyboard.Modifiers != ModifierKeys.None) return;

        MoveFocus(e.Key == Key.Left ? -1 : 1);
        e.Handled = true;
    }

    /// <summary>Moves keyboard focus one button left (-1) or right (1); it stops at the ends.</summary>
    internal void MoveFocus(int delta)
    {
        if (_buttons.Count == 0) return;

        int current = _buttons.FindIndex(b => ReferenceEquals(b.Button, FocusManager.GetFocusedElement(this)));
        _buttons[Math.Clamp(Math.Max(current, 0) + delta, 0, _buttons.Count - 1)].Button.Focus();
    }
}
