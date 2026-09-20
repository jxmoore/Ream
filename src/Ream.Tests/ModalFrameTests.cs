using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.Services;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>Help and About wear the same drawn title bar as the main window, in the theme's colors.</summary>
public class ModalFrameTests
{
    private static T Offscreen<T>(T window) where T : Window
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Ui.Settle();
        return window;
    }

    private static HelpWindow Help() => Offscreen(new HelpWindow(HelpContent.Build(AppConfig.DefaultKeybindings())));

    private static AboutWindow About() => Offscreen(new AboutWindow(AboutInfo.Create("C:/x/Foo.ream", "C:/x/config.json")));

    private static T Part<T>(Window window, string name) where T : class
    {
        window.ApplyTemplate();
        return (T)window.Template.FindName(name, window);
    }

    public static IEnumerable<object[]> Dialogs() => [["help"], ["about"]];

    private static Window Open(string which) => which == "help" ? Help() : About();

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void TheDialogHasNoNativeCaption_ButADrawnTitleBarWithItsTitle(string which) => Ui.Run(() =>
    {
        var window = Open(which);
        try
        {
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            var bar = Part<FrameworkElement>(window, "PART_TitleBar");
            Assert.Equal(32, bar.ActualHeight);
            Assert.Equal(window.Title, Part<TextBlock>(window, "PART_TitleText").Text);
            Assert.False(string.IsNullOrWhiteSpace(window.Title));
        }
        finally { window.Close(); }
    });

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void TheTitleBarUsesTheThemeColors_LikeTheAppsOwn(string which) => Ui.Run(() =>
    {
        var window = Open(which);
        try
        {
            var bar = Part<Grid>(window, "PART_TitleBar");
            Assert.Equal(Themes.Brush("WindowBackgroundBrush"), ((SolidColorBrush)bar.Background).Color);
            Assert.Equal(Themes.Brush("TextBrush"), ((SolidColorBrush)Part<TextBlock>(window, "PART_TitleText").Foreground).Color);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheTitleBarMatchesTheMainWindows_HeightAndCaptionButton() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var window = Help();
        try
        {
            var mainBar = (FrameworkElement)fx.Window.FindName("TitleBar");
            var mainClose = (Button)fx.Window.FindName("CloseButton");
            var close = Part<Button>(window, "PART_CloseButton");

            Assert.Equal(mainBar.ActualHeight, Part<FrameworkElement>(window, "PART_TitleBar").ActualHeight);
            Assert.Same(mainClose.Style, close.Style);
            Assert.Equal(mainClose.ActualWidth, close.ActualWidth);
            Assert.Equal(mainClose.Content, close.Content);
        }
        finally { window.Close(); }
    });

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void TheCloseButtonClosesTheDialog(string which) => Ui.Run(() =>
    {
        var window = Open(which);
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        var close = Part<Button>(window, "PART_CloseButton");
        ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)!).Invoke();
        Ui.Settle();

        Assert.True(closed);
    });

    [Fact]
    public void TheCloseCommand_ClosesWhicheverWindowItIsGiven_AndToleratesNone() => Ui.Run(() =>
    {
        var window = About();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        ModalChrome.CloseCommand.Execute(null);
        Assert.False(closed);

        ModalChrome.CloseCommand.Execute(window);
        Assert.True(closed);
    });

    [Fact]
    public void AboutStillShowsItsContent_InsideTheFrame() => Ui.Run(() =>
    {
        var window = About();
        try
        {
            Assert.Equal("Ream", ((TextBlock)window.FindName("NameText")).Text);
            Assert.Equal("C:/x/Foo.ream", ((TextBlock)window.FindName("FolderText")).Text);
        }
        finally { window.Close(); }
    });
}
