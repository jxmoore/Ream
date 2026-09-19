using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ream.App;
using Ream.App.Services;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>At very low canvas opacity Ream's own text sits on a plate in the theme's canvas color, so it stays readable.</summary>
public class ChromePlateTests
{
    [Theory]
    [InlineData(100, 0)]
    [InlineData(90, 22)]
    [InlineData(50, 108)]
    [InlineData(0, 217)]
    [InlineData(-5, 217)]
    [InlineData(150, 0)]
    public void ThePlateFadesInAsTheCanvasFadesOut(int opacity, int alpha) =>
        Assert.Equal(alpha, CanvasStyle.PlateAlpha(opacity));

    [Theory]
    [InlineData(100, 0)]
    [InlineData(50, 108)]
    [InlineData(0, 217)]
    public void ThePlateBrush_IsTheCanvasColorAtThatAlpha(int opacity, int alpha) => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current);
        try
        {
            theme.Apply("dracula", canvasOpacity: opacity);

            var plate = Themes.Brush(ThemeService.ChromePlateBrushKey);
            var canvas = Themes.Brush("WindowBackgroundBrush");
            Assert.Equal(alpha, plate.A);
            Assert.Equal((canvas.R, canvas.G, canvas.B), (plate.R, plate.G, plate.B));
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

    /// <summary>What shows where the plate is: the plate over a desktop pixel.</summary>
    private static Color Over(Color plate, Color desktop)
    {
        double a = plate.A / 255.0;
        byte Mix(byte p, byte d) => (byte)Math.Round(p * a + d * (1 - a));
        return Color.FromRgb(Mix(plate.R, desktop.R), Mix(plate.G, desktop.G), Mix(plate.B, desktop.B));
    }

    [Fact]
    public void InEveryTheme_TheTextStaysReadable_OverAnyDesktop_AtZeroOpacity() => Ui.Run(() =>
    {
        var theme = new ThemeService(Application.Current);
        try
        {
            foreach (var info in ThemeCatalog.All)
            {
                theme.Apply(info.Id, canvasOpacity: 0);
                var plate = Themes.Brush(ThemeService.ChromePlateBrushKey);
                var text = Themes.Brush("TextBrush");
                var muted = Themes.Brush("MutedTextBrush");

                foreach (var desktop in new[] { Colors.Black, Colors.White, Colors.Gray, Color.FromRgb(0x30, 0x60, 0xc0) })
                {
                    var behind = Over(plate, desktop);
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

    private static (byte A, byte R, byte G, byte B) PixelIn(WindowFixture fx, System.Windows.Media.Imaging.BitmapSource shot, FrameworkElement element, double x)
    {
        var p = element.TranslatePoint(new Point(x, element.ActualHeight / 2), fx.Window);
        return Ui.PixelAt(shot, (int)p.X, (int)p.Y);
    }

    [Fact]
    public void AtZeroInDracula_TheTextChromeHasAPlateBehindIt() => Ui.Run(() =>
    {
        using var fx = Docked();
        var service = new ThemeService(Application.Current, () => true);
        service.Apply("dracula", canvasOpacity: 0);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);
            var canvas = Themes.Brush("WindowBackgroundBrush");

            var tabs = new[] { "FileTab", "HomeTab", "ViewTab" }.Select(n => (FrameworkElement)fx.Window.FindName(n));
            var label = (FrameworkElement)fx.Window.FindName("WorkspaceLabel");
            var title = (FrameworkElement)fx.Window.FindName("TitleText");
            var groupLabel = Ui.Descendants<TextBlock>((FrameworkElement)fx.Window.FindName("Ribbon")).First(t => t.Text == "Font");

            foreach (var element in tabs.Append(label).Append(title).Append(groupLabel))
            {
                var pixel = PixelIn(fx, shot, element, 2);
                Assert.True(pixel.A >= 205, $"{element.GetType().Name} {(element as TextBlock)?.Text ?? element.Name}: plate alpha is {pixel.A}");
                Assert.InRange((int)pixel.R, canvas.R - 4, canvas.R + 4);
                Assert.InRange((int)pixel.G, canvas.G - 4, canvas.G + 4);
            }
        }
        finally
        {
            service.Apply("dark");
        }
    });

    [Fact]
    public void WhileTheCanvasIsSolid_ThePlateAddsNothing() => Ui.Run(() =>
    {
        using var fx = Docked();
        var service = new ThemeService(Application.Current, () => true);
        service.Apply("dracula", canvasOpacity: 100);
        try
        {
            Ui.Settle();
            var shot = Ui.Render(fx.Window);
            var canvas = Themes.Brush("WindowBackgroundBrush");

            var pixel = PixelIn(fx, shot, (FrameworkElement)fx.Window.FindName("FileTab"), 2);

            Assert.Equal(255, pixel.A);
            Assert.Equal((canvas.R, canvas.G, canvas.B), (pixel.R, pixel.G, pixel.B));
        }
        finally
        {
            service.Apply("dark");
        }
    });

    [Fact]
    public void TheCaptionButtons_AndTheSliders_AreBackedToo() => Ui.Run(() =>
    {
        using var fx = Docked();
        var service = new ThemeService(Application.Current, () => true);
        service.Apply("dracula", canvasOpacity: 0);
        try
        {
            fx.Window.SelectTab(RibbonTab.View);
            Ui.Settle();
            var shot = Ui.Render(fx.Window);

            var minimize = (FrameworkElement)fx.Window.FindName("MinimizeButton");
            Assert.True(PixelIn(fx, shot, minimize, 2).A >= 205, "the minimize button's glyph needs a plate");

            var caption = Ui.Descendants<TextBlock>((FrameworkElement)fx.Window.FindName("ViewRibbon")).First(t => t.Text == "Canvas");
            Assert.True(PixelIn(fx, shot, caption, 1).A >= 205, "the slider captions sit on a plate");
        }
        finally
        {
            service.Apply("dark");
        }
    });
}
