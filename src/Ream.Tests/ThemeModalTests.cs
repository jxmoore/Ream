using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>The View tab's Theme button opens this: Layout, Theme and Opacity, the same SettingsViewModel the three separate ribbon groups used to edit.</summary>
public class ThemeModalTests
{
    private static SettingsViewModel Make(AppViewModel app, ThemeService theme, bool supported = true) =>
        new(app, theme, store: null, dispatcher: null, blurSupported: () => supported);

    private static ComboBox BoxNamed(ThemeModal modal, string name) => (ComboBox)modal.FindName(name);

    /// <summary>Hosts the modal off-screen for the length of a test, without a real modal loop.</summary>
    private static ThemeModal Shown(SettingsViewModel settings)
    {
        var modal = new ThemeModal(settings);
        modal.WindowStartupLocation = WindowStartupLocation.Manual;
        modal.Left = -32000;
        modal.Top = -32000;
        modal.ShowActivated = false;
        modal.Show();
        Ui.Settle();
        return modal;
    }

    [Fact]
    public void TheGapBox_HasPresetsAndTheCurrentValue_AndACustomTypedValueCommitsOnLostFocus() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var modal = Shown(settings);
            try
            {
                var box = BoxNamed(modal, "GapBox");
                Assert.True(box.IsEditable);
                Assert.Equal("28", box.Text);
                Assert.Contains(20.0, box.Items.Cast<double>());

                box.Text = "16";
                box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                Ui.Settle();

                Assert.Equal(16, app.Config.Layout.GapPx);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheOpacityBoxes_HavePresetsAndDriveTheTwoOpacities() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var modal = Shown(settings);
            try
            {
                var canvas = BoxNamed(modal, "CanvasOpacityBox");
                var notes = BoxNamed(modal, "NoteOpacityBox");
                Assert.True(canvas.IsEditable && notes.IsEditable);
                Assert.Equal("100", canvas.Text);
                Assert.Equal("100", notes.Text);
                Assert.Contains(50, canvas.Items.Cast<int>());

                canvas.Text = "30";
                canvas.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                notes.Text = "60";
                notes.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                Ui.Settle();

                Assert.Equal(30, app.Config.CanvasOpacity);
                Assert.Equal(60, app.Config.NoteOpacity);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void OnWindowsWithoutBlur_TheCanvasOpacityBoxTooltipSaysWhatIsMissing() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => false);
        var settings = Make(app, theme, supported: false);
        try
        {
            var modal = Shown(settings);
            try
            {
                var canvas = BoxNamed(modal, "CanvasOpacityBox");
                Assert.True(canvas.IsEnabled);
                Assert.Contains("Windows 10", (string)canvas.ToolTip);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheCenterFocusedCheckBox_ChangesTheLayout() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var modal = Shown(settings);
            try
            {
                var center = (CheckBox)modal.FindName("CenterFocusedCheckBox");
                Assert.True(center.IsChecked);

                center.IsChecked = false;
                Ui.Settle();

                Assert.False(app.Config.Layout.CenterFocusedColumn);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheThemeList_HasOneRadioPerTheme_WithTheCurrentOneChecked() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig { Theme = "gruvbox" }, []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var modal = Shown(settings);
            try
            {
                var radios = Ui.Descendants<RadioButton>(modal).ToList();

                Assert.Equal(ThemeCatalog.All.Count, radios.Count);
                Assert.Contains(Ui.Descendants<TextBlock>(modal), t => t.Text == "Gruvbox");
                Assert.Single(radios, r => r.IsChecked == true);
                Assert.Equal("gruvbox", ((ThemeOption)radios.Single(r => r.IsChecked == true).DataContext).Id);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void PickingAThemeRadio_AppliesItAtOnce() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var modal = Shown(settings);
            try
            {
                var dracula = Ui.Descendants<RadioButton>(modal).Single(r => ((ThemeOption)r.DataContext).Id == "dracula");

                dracula.IsChecked = true;
                Ui.Settle();

                Assert.Equal("dracula", app.Config.Theme);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheThemeList_FollowsTheConfigWhenItChangesElsewhere() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var theme = new ThemeService(Application.Current, () => true);
        var settings = Make(app, theme);
        try
        {
            var modal = Shown(settings);
            try
            {
                app.Config = app.Config.With(theme: "nord");
                Ui.Settle();

                var radios = Ui.Descendants<RadioButton>(modal).ToList();
                Assert.Equal("nord", ((ThemeOption)radios.Single(r => r.IsChecked == true).DataContext).Id);
            }
            finally
            {
                modal.Close();
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });
}
