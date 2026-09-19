using System.Windows;
using System.Windows.Media;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.Core.Models;

namespace Ream.Tests;

public class ChromePlannerTests
{
    private static readonly Color Canvas = Color.FromRgb(0x15, 0x15, 0x1a);
    private static readonly Color Text = Color.FromRgb(0xe8, 0xe8, 0xee);

    private static WindowAppearance Solid(bool light = false) => new(Canvas, Text, light, SeeThrough: false);
    private static WindowAppearance Glass(bool light = false) => new(Canvas, Text, light, SeeThrough: true);

    [Fact]
    public void ColorRefs_AreBlueGreenRed()
    {
        Assert.Equal(0x001A1515u, ChromePlanner.ToColorRef(Canvas));
        Assert.Equal(0x00FF0000u, ChromePlanner.ToColorRef(Color.FromRgb(0, 0, 255)));
        Assert.Equal(0x000000FFu, ChromePlanner.ToColorRef(Color.FromRgb(255, 0, 0)));
    }

    [Fact]
    public void OnWindows11_ASolidCanvasGetsATitleBarTheSameColor()
    {
        var plan = ChromePlanner.Plan(Solid(), 26100);

        Assert.Equal(ChromePlanner.ToColorRef(Canvas), plan.CaptionColor);
        Assert.Equal(ChromePlanner.ToColorRef(Canvas), plan.BorderColor);
        Assert.Equal(ChromePlanner.ToColorRef(Text), plan.TextColor);
        Assert.Equal(ChromePlanner.BackdropNone, plan.BackdropType);
        Assert.False(plan.ExtendFrameIntoClient);
        Assert.False(plan.TransparentCanvas);
    }

    [Fact]
    public void WhenSeeThrough_TheBlurIsOn_AndTheTitleBarSharesIt()
    {
        var plan = ChromePlanner.Plan(Glass(), 26100);

        Assert.Equal(ChromePlanner.BackdropAcrylic, plan.BackdropType);
        Assert.True(plan.ExtendFrameIntoClient);
        Assert.True(plan.TransparentCanvas);
        Assert.Equal(ChromePlanner.ColorDefault, plan.CaptionColor);
        Assert.Equal(ChromePlanner.ColorDefault, plan.BorderColor);
        Assert.Equal(ChromePlanner.ToColorRef(Text), plan.TextColor);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheTitleBarFollowsLightAndDark(bool light, bool expectedDark)
    {
        Assert.Equal(expectedDark, ChromePlanner.Plan(Solid(light), 26100).DarkMode);
    }

    [Fact]
    public void BeforeWindows11_22H2_ThereIsNoBlur_EvenIfAskedFor()
    {
        var plan = ChromePlanner.Plan(Glass(), 22000);

        Assert.Null(plan.BackdropType);
        Assert.False(plan.ExtendFrameIntoClient);
        Assert.False(plan.TransparentCanvas);
        Assert.Equal(ChromePlanner.ToColorRef(Canvas), plan.CaptionColor);
    }

    [Fact]
    public void OnWindows11_21H2_OnlyTheColorsApply()
    {
        var plan = ChromePlanner.Plan(Solid(), 22000);

        Assert.NotNull(plan.CaptionColor);
        Assert.NotNull(plan.TextColor);
        Assert.NotNull(plan.BorderColor);
        Assert.Null(plan.BackdropType);
    }

    [Fact]
    public void OnWindows10_OnlyTheDarkModeSwitchApplies()
    {
        var plan = ChromePlanner.Plan(Solid(), 19045);

        Assert.True(plan.DarkMode);
        Assert.Null(plan.CaptionColor);
        Assert.Null(plan.TextColor);
        Assert.Null(plan.BorderColor);
        Assert.Null(plan.BackdropType);
    }

    [Fact]
    public void OnOlderBuilds_NothingIsTouched()
    {
        var plan = ChromePlanner.Plan(Glass(), 18362);

        Assert.Equal(default, plan with { ExtendFrameIntoClient = false, TransparentCanvas = false });
        Assert.False(plan.ExtendFrameIntoClient);
        Assert.False(plan.TransparentCanvas);
    }

    [Theory]
    [InlineData(19040, false)]
    [InlineData(19041, true)]
    [InlineData(22000, true)]
    public void TheDarkModeSwitchStartsAtWindows10_2004(int build, bool has)
    {
        Assert.Equal(has, ChromePlanner.Plan(Solid(), build).DarkMode is not null);
    }

    [Theory]
    [InlineData(19041, false)]
    [InlineData(21999, false)]
    [InlineData(22000, true)]
    [InlineData(26100, true)]
    public void TheTitleBarColorsStartAtWindows11(int build, bool has)
    {
        Assert.Equal(has, ChromePlanner.Plan(Solid(), build).CaptionColor is not null);
    }

    [Theory]
    [InlineData(22000, false)]
    [InlineData(22620, false)]
    [InlineData(22621, true)]
    [InlineData(26100, true)]
    public void TheBackdropSwitchStartsAtWindows11_22H2(int build, bool has)
    {
        Assert.Equal(has, ChromePlanner.Plan(Solid(), build).BackdropType is not null);
    }

    [Fact]
    public void TheBackdropBuildMatchesWhatTheThemeServiceCallsSupported()
    {
        Assert.Equal(SystemBackdropSupport.MinimumBuild, ChromePlanner.BackdropBuild);
        Assert.Equal(22621, SystemBackdropSupport.MinimumBuild);
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
    public void FollowingTheTheme_AppliesItNow_AndOnEveryChange() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var backdrop = new FakeBackdrop();
        var window = Window(app, backdrop);
        var theme = new ThemeService(Application.Current, () => true);
        try
        {
            theme.Apply("dark");
            window.FollowTheme(theme);
            Assert.Single(backdrop.Applied);
            Assert.False(backdrop.Applied[^1].IsLight);
            Assert.False(backdrop.Applied[^1].SeeThrough);

            theme.Apply("light");
            Assert.Equal(2, backdrop.Applied.Count);
            Assert.True(backdrop.Applied[^1].IsLight);
            Assert.Equal(Themes.Brush("WindowBackgroundBrush"), backdrop.Applied[^1].Canvas);

            theme.Apply("dracula", canvasOpacity: 40);
            Assert.Equal(3, backdrop.Applied.Count);
            Assert.True(backdrop.Applied[^1].SeeThrough);
            Assert.Equal(255, backdrop.Applied[^1].Canvas.A);
            Assert.Equal(Themes.Brush("TextBrush"), backdrop.Applied[^1].Text);

            theme.Apply("dracula", canvasOpacity: 40);
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
        var appearance = new WindowAppearance(Colors.Black, Colors.White, false, false);

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

    [Fact]
    public void TheWindowBackground_IsTheCanvasBrush_SoItFadesWithTheOpacity() => Ui.Run(() =>
    {
        var app = new AppViewModel(new AppConfig(), []);
        var window = Window(app, new FakeBackdrop());
        var theme = new ThemeService(Application.Current, () => true);
        try
        {
            window.Show();
            theme.Apply("dark", canvasOpacity: 50);
            Ui.Settle();

            Assert.Equal(128, ((SolidColorBrush)window.Background).Color.A);

            theme.Apply("dark");
            Ui.Settle();
            Assert.Equal(255, ((SolidColorBrush)window.Background).Color.A);
        }
        finally
        {
            theme.Apply("dark");
            window.Close();
        }
    });
}
