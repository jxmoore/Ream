using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class BlurPlanTests
{
    private static readonly WindowAppearance Blurred = new(SeeThrough: true, Blur: true);
    private static readonly WindowAppearance Clear = new(SeeThrough: true, Blur: false);
    private static readonly WindowAppearance Solid = new(SeeThrough: false, Blur: false);

    [Fact]
    public void BlurIsAskedFor_WhenWantedAndTheWindowsBuildHasIt()
    {
        Assert.Equal(BlurPlan.BlurBehind, BlurPlan.For(Blurred, 26100).AccentState);
        Assert.Equal(BlurPlan.BlurBehind, BlurPlan.For(Blurred, 19045).AccentState);
    }

    [Theory]
    [InlineData(17133, BlurPlan.Disabled)]
    [InlineData(17134, BlurPlan.BlurBehind)]
    [InlineData(16299, BlurPlan.Disabled)]
    public void ItStartsAtWindows10_1803(int build, int expected)
    {
        Assert.Equal(expected, BlurPlan.For(Blurred, build).AccentState);
        Assert.Equal(17134, BlurSupport.MinimumBuild);
    }

    [Fact]
    public void WithoutBlurWanted_NothingIsAskedFor()
    {
        Assert.Equal(BlurPlan.Disabled, BlurPlan.For(Clear, 26100).AccentState);
        Assert.Equal(BlurPlan.Disabled, BlurPlan.For(Solid, 26100).AccentState);
    }
}

public class WindowChromeWiringTests
{
    private sealed class FakeBackdrop : IWindowBackdrop
    {
        public List<WindowAppearance> Applied { get; } = [];

        public void Apply(WindowAppearance appearance) => Applied.Add(appearance);
    }

    private static Ream.App.MainWindow Window(AppViewModel app, FakeBackdrop backdrop) =>
        new(app, settings: null, backdrop)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };

    [Fact]
    public void FollowingTheTheme_AsksForBlurNow_AndWheneverItChanges() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var backdrop = new FakeBackdrop();
        var window = Window(app, backdrop);
        var theme = new ThemeService(Application.Current, () => true);
        try
        {
            theme.Apply("dark");
            window.FollowTheme(theme);
            Assert.Equal([new WindowAppearance(false, false)], backdrop.Applied);

            theme.Apply("dracula", canvasOpacity: 40);
            Assert.Equal(new WindowAppearance(true, true), backdrop.Applied[^1]);
            Assert.Equal(2, backdrop.Applied.Count);

            theme.Apply("dracula", canvasOpacity: 40, canvasBlur: false);
            Assert.Equal(new WindowAppearance(true, false), backdrop.Applied[^1]);

            theme.Apply("dracula", canvasOpacity: 40, canvasBlur: false);
            Assert.Equal(3, backdrop.Applied.Count);
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    [Fact]
    public void TheAppearanceIsAppliedAgainOnceTheWindowExists() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var backdrop = new FakeBackdrop();
        var window = Window(app, backdrop);
        var appearance = new WindowAppearance(true, true);

        window.ApplyAppearance(appearance);
        Assert.Single(backdrop.Applied);

        window.Show();
        try
        {
            Ui.Settle();
            Assert.Equal(2, backdrop.Applied.Count);
            Assert.Equal(appearance, backdrop.Applied[^1]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void WithoutAnAppearanceYet_NothingIsAppliedWhenTheWindowOpens() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var backdrop = new FakeBackdrop();
        var window = Window(app, backdrop);

        window.Show();
        try
        {
            Ui.Settle();
            Assert.Empty(backdrop.Applied);
        }
        finally
        {
            window.Close();
        }
    });
}

public class OwnWindowFrameTests
{
    private static TextBlock TitleOf(Ream.App.MainWindow window) => (TextBlock)window.FindName("TitleText");

    [Fact]
    public void TheWindowIsBorderlessAndTransparent_ReadyForOurOwnFrame() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        Assert.Equal(WindowStyle.None, fx.Window.WindowStyle);
        Assert.True(fx.Window.AllowsTransparency);
        Assert.Equal(ResizeMode.CanResize, fx.Window.ResizeMode);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)fx.Window.Background).Color);
    });

    [Fact]
    public void TheFrameCanStillBeDraggedAndResized_LikeANativeOne() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        var chrome = WindowChrome.GetWindowChrome(fx.Window);

        Assert.NotNull(chrome);
        Assert.Equal(32, chrome.CaptionHeight);
        Assert.Equal(6, chrome.ResizeBorderThickness.Left);
        Assert.Equal(0, chrome.GlassFrameThickness.Top);
        Assert.False(chrome.UseAeroCaptionButtons);
    });

    [Fact]
    public void TheTitleBarShowsTheWindowTitle_AndHasMinimizeMaximizeAndClose() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        Assert.Equal("Ream", TitleOf(fx.Window).Text);
        var buttons = new[] { "MinimizeButton", "MaximizeButton", "CloseButton" }
            .Select(n => (Button)fx.Window.FindName(n)).ToList();

        Assert.Equal(["Minimize", "Maximize", "Close"], buttons.Select(b => (string)b.ToolTip));
        Assert.All(buttons, b => Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(b), $"{b.Name} must take clicks"));
        Assert.All(buttons, b => Assert.Equal(46, b.Width));
    });

    [Fact]
    public void ClosingFromTheTitleBar_ClosesTheWindow() => Ui.Run(() =>
    {
        var fx = new WindowFixture(("W", 1));
        bool closed = false;
        fx.Window.Closed += (_, _) => closed = true;

        ((Button)fx.Window.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Settle();

        Assert.True(closed);
        fx.Dir.Dispose();
    });

    [Fact]
    public void InAppFullscreen_TheTitleBarGoesAway_AndComesBack() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));
        var titleBar = (Grid)fx.Window.FindName("TitleBar");
        Assert.Equal(Visibility.Visible, titleBar.Visibility);

        fx.Window.ApplyFullscreenChrome(true);
        Assert.Equal(Visibility.Collapsed, titleBar.Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)fx.Window.FindName("TabRow")).Visibility);

        fx.Window.ApplyFullscreenChrome(false);
        Assert.Equal(Visibility.Visible, titleBar.Visibility);
    });

    [Fact]
    public void OnlyTheCloseButtonHasTheRedHover() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        var style = (Style)fx.Window.FindResource("CaptionButtonStyle");
        var template = (ControlTemplate)style.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;

        var trigger = template.Triggers.OfType<MultiTrigger>().Single();
        Assert.Contains(trigger.Conditions, c => Equals(c.Value, "close"));
        Assert.Equal("close", ((Button)fx.Window.FindName("CloseButton")).Tag);
        Assert.Null(((Button)fx.Window.FindName("MinimizeButton")).Tag);
    });
}

/// <summary>What the user actually sees: the color and the transparency of each part of the window, read from its pixels.</summary>
public class ChromeLooksTests
{
    private static ThemeService Apply(string theme, int opacity, out Action restore)
    {
        var service = new ThemeService(Application.Current, () => true);
        service.Apply(theme, opacity);
        restore = () => service.Apply("dark");
        return service;
    }

    private static (byte A, byte R, byte G, byte B) Sample(WindowFixture fx, BitmapSource shot, string region, double x = 0.5)
    {
        var element = (FrameworkElement)fx.Window.FindName(region);
        var (px, py) = Ui.CenterOf(element, fx.Window, x);
        return Ui.PixelAt(shot, px, py);
    }

    private static (byte A, byte R, byte G, byte B) SampleCanvas(WindowFixture fx, BitmapSource shot)
    {
        // The far left of the canvas, beside the notes.
        var canvas = fx.Canvas;
        var p = canvas.TranslatePoint(new Point(12, canvas.ActualHeight / 2), fx.Window);
        return Ui.PixelAt(shot, (int)p.X, (int)p.Y);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("dracula")]
    [InlineData("catppuccin")]
    [InlineData("material")]
    [InlineData("nord")]
    [InlineData("gruvbox")]
    public void TheTitleBarIsExactlyTheCanvasColor_InEveryTheme(string theme) => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        Apply(theme, 100, out var restore);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);
            var expected = Themes.Brush("WindowBackgroundBrush");

            foreach (var region in new[] { "TitleBar", "TabRow" })
            {
                var pixel = Sample(fx, shot, region, 0.45);
                Assert.Equal((byte)255, pixel.A);
                Assert.Equal((expected.R, expected.G, expected.B), (pixel.R, pixel.G, pixel.B));
            }

            var canvas = SampleCanvas(fx, shot);
            Assert.Equal((expected.R, expected.G, expected.B), (canvas.R, canvas.G, canvas.B));
        }
        finally
        {
            restore();
        }
    });

    [Theory]
    [InlineData(100, 255)]
    [InlineData(50, 128)]
    [InlineData(25, 64)]
    [InlineData(0, 1)]
    public void EveryPartOfTheCanvasFadesTogether_WithoutStackingUp(int opacity, int alpha) => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        Apply("dark", opacity, out var restore);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);

            Assert.Equal(alpha, Sample(fx, shot, "TitleBar", 0.45).A);
            Assert.Equal(alpha, Sample(fx, shot, "TabRow", 0.45).A);
            Assert.Equal(alpha, SampleCanvas(fx, shot).A);
        }
        finally
        {
            restore();
        }
    });

    [Fact]
    public void AtZero_TheWindowIsAsClearAsItCanBeWithoutLettingClicksFallThrough() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        Apply("dark", 0, out var restore);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);

            var title = Sample(fx, shot, "TitleBar", 0.45);
            Assert.InRange((int)title.A, 1, 2);
        }
        finally
        {
            restore();
        }
    });

    [Fact]
    public void TheRibbonPanel_FollowsTheCanvas_ButStaysReadable() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        Apply("dark", 0, out var restore);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);

            // Far right of the panel, past the last group of buttons.
            var panel = (FrameworkElement)fx.Window.FindName("RibbonPanel");
            var p = panel.TranslatePoint(new Point(panel.ActualWidth - 20, panel.ActualHeight / 2), fx.Window);
            var pixel = Ui.PixelAt(shot, (int)p.X, (int)p.Y);

            Assert.Equal(ThemeService.RibbonMinimumAlpha, (int)pixel.A);
        }
        finally
        {
            restore();
        }
    });

    [Fact]
    public void TheColorsFollowATheme_WithoutRebuildingTheWindow() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 2));
        var service = new ThemeService(Application.Current);
        try
        {
            service.Apply("light");
            Ui.Settle();
            var light = Sample(fx, Ui.Render(fx.Window), "TitleBar", 0.45);

            service.Apply("nord");
            Ui.Settle();
            var nord = Sample(fx, Ui.Render(fx.Window), "TitleBar", 0.45);

            Assert.NotEqual((light.R, light.G, light.B), (nord.R, nord.G, nord.B));
        }
        finally
        {
            service.Apply("dark");
        }
    });

    [Fact]
    public void TheWindowItselfPaintsNothing_SoThereIsNothingToStackWith() => Ui.Run(() =>
    {
        using var fx = new WindowFixture(("W", 1));

        Assert.Equal((byte)0, ((SolidColorBrush)fx.Window.Background).Color.A);
    });
}
