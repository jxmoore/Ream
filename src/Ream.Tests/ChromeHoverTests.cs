using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ream.App;
using Ream.App.Services;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>
/// At very low canvas opacity Ream's own text floats bare over the desktop; while the pointer is on the title bar, the tab
/// row, the ribbon or the workspace label, a backdrop appears behind it so it is readable when you reach for it.
/// </summary>
public class ChromeHoverTests
{
    [Theory]
    [InlineData(100, 255)]
    [InlineData(80, 204)]
    [InlineData(50, 128)]
    [InlineData(30, 152)]
    [InlineData(0, 217)]
    [InlineData(-5, 217)]
    [InlineData(150, 255)]
    public void TheHoverBackdrop_IsAtLeastAsSolidAsTheCanvas_AndGrowsAsTheCanvasFades(int opacity, int alpha) =>
        Assert.Equal(alpha, CanvasStyle.HoverBackdropAlpha(opacity));

    [Theory]
    [InlineData(100, 255)]
    [InlineData(0, 217)]
    public void TheHoverBrushes_AreTheCanvasAndToolbarColorsAtThatAlpha(int opacity, int alpha) => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current);
        try
        {
            theme.Apply("dracula", canvasOpacity: opacity);

            var chrome = Themes.Brush(ThemeService.ChromeHoverBrushKey);
            var canvas = Themes.Brush("WindowBackgroundBrush");
            Assert.Equal(alpha, chrome.A);
            Assert.Equal((canvas.R, canvas.G, canvas.B), (chrome.R, chrome.G, chrome.B));

            var ribbon = Themes.Brush(ThemeService.RibbonHoverBrushKey);
            var toolbar = Themes.Brush("ToolbarBrush");
            Assert.Equal(alpha, ribbon.A);
            Assert.Equal((toolbar.R, toolbar.G, toolbar.B), (ribbon.R, ribbon.G, ribbon.B));
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static Color Over(Color backdrop, Color desktop)
    {
        double a = backdrop.A / 255.0;
        byte Mix(byte p, byte d) => (byte)Math.Round(p * a + d * (1 - a));
        return Color.FromRgb(Mix(backdrop.R, desktop.R), Mix(backdrop.G, desktop.G), Mix(backdrop.B, desktop.B));
    }

    [Fact]
    public void InEveryTheme_HoveredTextIsReadable_OverAnyDesktop_AtZeroOpacity() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current);
        try
        {
            foreach (var info in ThemeCatalog.All)
            {
                theme.Apply(info.Id, canvasOpacity: 0);
                var text = Themes.Brush("TextBrush");
                var muted = Themes.Brush("MutedTextBrush");
                var backdrops = new[] { Themes.Brush(ThemeService.ChromeHoverBrushKey), Themes.Brush(ThemeService.RibbonHoverBrushKey) };

                foreach (var backdrop in backdrops)
                foreach (var desktop in new[] { Colors.Black, Colors.White, Colors.Gray, Color.FromRgb(0x30, 0x60, 0xc0) })
                {
                    var behind = Over(backdrop, desktop);
                    Assert.True(Contrast(text, behind) >= 4.5, $"{info.Id}: text over {desktop} is {Contrast(text, behind):0.0}:1");
                    Assert.True(Contrast(muted, behind) >= 3.0, $"{info.Id}: muted text over {desktop} is {Contrast(muted, behind):0.0}:1");
                }
            }
        }
        finally
        {
            theme.Apply("dark");
        }
    });

    // ----- In the real window -----

    private static WindowFixture Docked() =>
        new(new AppConfig { Ribbon = new RibbonConfig { AutoHide = false } }, ("W", 2));

    /// <summary>The pixel in the top row of an element, halfway along: above the glyphs of any text in it.</summary>
    private static (byte A, byte R, byte G, byte B) TopPixel(WindowFixture fx, System.Windows.Media.Imaging.BitmapSource shot, FrameworkElement element)
    {
        var p = element.TranslatePoint(new Point(element.ActualWidth / 2, 0.5), fx.Window);
        return Ui.PixelAt(shot, (int)p.X, (int)p.Y);
    }

    [Fact]
    public void WhenNothingIsHovered_TheChromeIsAsClearAsTheCanvas() => Ui.Run(() =>
    {
        using var fx = Docked();
        var service = new ThemeService(Application.Current, () => true);
        service.Apply("dracula", canvasOpacity: 0);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);

            var tabs = new[] { "FileTab", "HomeTab", "ViewTab" }.Select(n => (FrameworkElement)fx.Window.FindName(n));
            var label = (FrameworkElement)fx.Window.FindName("WorkspaceLabel");
            var title = (FrameworkElement)fx.Window.FindName("TitleText");
            var groupLabel = Ui.Descendants<TextBlock>((FrameworkElement)fx.Window.FindName("Ribbon")).First(t => t.Text == "Font");

            foreach (var element in tabs.Append(label).Append(title).Append(groupLabel))
                Assert.InRange((int)TopPixel(fx, shot, element).A, 0, 3);
        }
        finally
        {
            service.Apply("dark");
        }
    });

    /// <summary>The key of the brush a style's hover trigger puts behind the element, or null if it has none.</summary>
    private static object? HoverBackdropKey(FrameworkElement element)
    {
        var trigger = element.Style?.Triggers.OfType<Trigger>().FirstOrDefault(t => t.Property == UIElement.IsMouseOverProperty);
        var setter = trigger?.Setters.OfType<Setter>().FirstOrDefault(s => s.Property.Name == "Background");
        return (setter?.Value as System.Windows.DynamicResourceExtension)?.ResourceKey;
    }

    [Theory]
    [InlineData("TitleBar", "ChromeHoverBrush")]
    [InlineData("TabRow", "ChromeHoverBrush")]
    [InlineData("RibbonPanel", "RibbonHoverBrush")]
    [InlineData("WorkspaceLabel", "ChromeHoverBrush")]
    public void EachPieceOfChrome_GetsItsBackdropWhileHovered(string name, string brush) => Ui.Run(() =>
    {
        using var fx = Docked();
        var element = (FrameworkElement)fx.Window.FindName(name);

        Assert.Equal(brush, HoverBackdropKey(element));
    });

    [Fact]
    public void AtZeroInDracula_TheHoverBackdropsAreNearlySolid_ButTheIdleOnesAreNot() => Ui.Run(() =>
    {
        using var fx = Docked();
        var service = new ThemeService(Application.Current, () => true);
        service.Apply("dracula", canvasOpacity: 0);
        try
        {
            Assert.Equal(217, ((SolidColorBrush)fx.Window.FindResource("ChromeHoverBrush")).Color.A);
            Assert.Equal(217, ((SolidColorBrush)fx.Window.FindResource("RibbonHoverBrush")).Color.A);
            Assert.Equal(1, ((SolidColorBrush)fx.Window.FindResource("CanvasBrush")).Color.A);
            Assert.Equal(1, ((SolidColorBrush)fx.Window.FindResource("RibbonBrush")).Color.A);
        }
        finally
        {
            service.Apply("dark");
        }
    });

    [Fact]
    public void WhileTheCanvasIsSolid_HoveringChangesNothingVisible() => Ui.Run(() =>
    {
        var service = new ThemeService(Application.Current, () => true);
        service.Apply("dracula", canvasOpacity: 100);
        try
        {
            Assert.Equal(((SolidColorBrush)Application.Current.Resources["CanvasBrush"]).Color, ((SolidColorBrush)Application.Current.Resources["ChromeHoverBrush"]).Color);
            Assert.Equal(((SolidColorBrush)Application.Current.Resources["RibbonBrush"]).Color, ((SolidColorBrush)Application.Current.Resources["RibbonHoverBrush"]).Color);
        }
        finally
        {
            service.Apply("dark");
        }
    });
}
