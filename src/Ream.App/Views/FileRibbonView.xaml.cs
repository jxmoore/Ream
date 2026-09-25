using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.App.Views;

/// <summary>
/// The File tab of the ribbon: New / Open / Save / Save As, the auto-save switch, Clear, and Help / About. The ream buttons are
/// bound to the app's commands; their tooltips carry the gesture bound right now and follow a config reload. Help and About are
/// raised as events because opening a window is the main window's job.
/// </summary>
public partial class FileRibbonView : UserControl
{
    private AppViewModel? _app;

    public FileRibbonView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public event Action? HelpRequested;

    public event Action? AboutRequested;

    private void OnHelp(object sender, RoutedEventArgs e) => HelpRequested?.Invoke();

    private void OnAbout(object sender, RoutedEventArgs e) => AboutRequested?.Invoke();

    /// <summary>The switch flips itself when clicked, but the manager decides: once the command has run, show what the app really says.</summary>
    private void OnAutoSaveClick(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_app is not null) AutoSaveToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, _app.AutoSave);
        });

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_app is not null) _app.PropertyChanged -= OnAppChanged;
        _app = e.NewValue as AppViewModel;
        if (_app is not null) _app.PropertyChanged += OnAppChanged;
        RefreshTooltips();
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.Config)) RefreshTooltips();
    }

    private void RefreshTooltips()
    {
        Tip(NewButton, "New ream", "newReam");
        Tip(OpenButton, "Open a ream", "openReam");
        Tip(SaveButton, "Save the ream", "save");
        Tip(SaveAsButton, "Save the ream under another name", "saveAs");
        Tip(ClearButton, "Remove every workspace and note (asks first; removed notes are kept in the .trash folder)", "clearReam");

        var others = OtherRecentReams();
        RecentButton.IsEnabled = others.Count > 0;
        RecentButton.ToolTip = others.Count > 0 ? "Reams opened, saved or created recently" : "No other reams yet";
    }

    private void Tip(Button button, string text, string action) =>
        button.ToolTip = _app is not null && _app.Config.Keybindings.TryGetValue(action, out var gesture) && !string.IsNullOrWhiteSpace(gesture)
            ? $"{text} ({GestureText.Pretty(gesture)})"
            : text;

    /// <summary>The Recent list, newest first, without whichever ream is open right now (reopening it would be a no-op).</summary>
    internal List<string> OtherRecentReams() => _app is null
        ? []
        : _app.Config.RecentReams.Where(p => !string.Equals(p, _app.Config.LastReam, StringComparison.OrdinalIgnoreCase)).ToList();

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (_app is null) return;

        var menu = new ContextMenu { PlacementTarget = RecentButton, Placement = PlacementMode.Bottom };
        foreach (string path in OtherRecentReams())
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileNameWithoutExtension(path), ToolTip = path };
            item.Click += (_, _) => _app.OpenRecentReamCommand.Execute(path);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0) menu.IsOpen = true;
    }
}
