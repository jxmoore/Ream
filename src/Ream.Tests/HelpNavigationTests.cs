using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.Views;
using Ream.Core.Layout;
using Ream.Core.Models;

namespace Ream.Tests;

public class HelpNavigatorTests
{
    [Fact]
    public void ItStartsOnTheFirstSection()
    {
        var nav = new HelpNavigator(3);

        Assert.Equal(0, nav.SectionIndex);
        Assert.False(nav.OnClose);
    }

    [Fact]
    public void DownStepsThroughTheSections_ThenOntoClose_AndStopsThere()
    {
        var nav = new HelpNavigator(3);

        Assert.True(nav.Down());
        Assert.True(nav.Down());
        Assert.Equal(2, nav.SectionIndex);
        Assert.True(nav.Down());

        Assert.True(nav.OnClose);
        Assert.Null(nav.SectionIndex);
        Assert.False(nav.Down());
        Assert.True(nav.OnClose);
    }

    [Fact]
    public void UpGoesBack_FromCloseToTheLastSection_AndStopsAtTheFirst()
    {
        var nav = new HelpNavigator(2);
        nav.Down();
        nav.Down();

        Assert.True(nav.Up());
        Assert.Equal(1, nav.SectionIndex);
        Assert.True(nav.Up());
        Assert.Equal(0, nav.SectionIndex);
        Assert.False(nav.Up());
        Assert.Equal(0, nav.SectionIndex);
    }

    [Fact]
    public void WithNoSections_CloseIsTheOnlyStop()
    {
        var nav = new HelpNavigator(0);

        Assert.True(nav.OnClose);
        Assert.False(nav.Down());
        Assert.False(nav.Up());
    }
}

public class HelpWindowNavigationTests
{
    private static HelpWindow Show(IReadOnlyDictionary<string, string>? keybindings = null, double height = 680)
    {
        var config = keybindings ?? AppConfig.DefaultKeybindings();
        var window = new HelpWindow(HelpContent.Build(config), keybindings)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Height = height,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        Ui.Settle();
        return window;
    }

    private static List<Border> Cards(HelpWindow window) =>
        Ui.Descendants<Border>(window).Where(b => b.Name == "Card").ToList();

    private static Color BorderColor(Border card) => ((SolidColorBrush)card.BorderBrush).Color;

    private static bool IsHighlighted(Border card) =>
        card.BorderThickness.Left == 2 && BorderColor(card) == Themes.Brush("FocusBorderBrush");

    private static Button Close(HelpWindow window) => (Button)window.FindName("CloseButton");

    private static bool CloseIsHighlighted(HelpWindow window) =>
        Close(window).BorderThickness.Left == 2 && ((SolidColorBrush)Close(window).BorderBrush).Color == Themes.Brush("FocusBorderBrush");

    [Fact]
    public void TheFirstSectionStartsHighlighted_LikeTheFocusedNote() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            var cards = Cards(window);

            Assert.Equal(6, cards.Count);
            Assert.True(IsHighlighted(cards[0]));
            Assert.All(cards.Skip(1), c => Assert.False(IsHighlighted(c)));
            Assert.False(CloseIsHighlighted(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Down_MovesTheHighlightToTheNextSection() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            window.NavigateDown();
            Ui.Settle();

            var cards = Cards(window);
            Assert.False(IsHighlighted(cards[0]));
            Assert.True(IsHighlighted(cards[1]));
            Assert.Equal(1, window.HighlightedSection);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PastTheLastSection_TheCloseButtonIsHighlighted_AndNoSectionIs() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            for (int i = 0; i < 6; i++) window.NavigateDown();
            Ui.Settle();

            Assert.True(window.CloseHighlighted);
            Assert.True(CloseIsHighlighted(window));
            Assert.All(Cards(window), c => Assert.False(IsHighlighted(c)));
            Assert.True(Close(window).IsKeyboardFocused || Close(window).IsFocused || Keyboard.FocusedElement == Close(window));

            window.NavigateDown(); // nothing past Close
            Assert.True(window.CloseHighlighted);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Up_FromClose_GoesBackToTheLastSection() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            for (int i = 0; i < 6; i++) window.NavigateDown();

            window.NavigateUp();
            Ui.Settle();

            Assert.False(window.CloseHighlighted);
            Assert.False(CloseIsHighlighted(window));
            Assert.True(IsHighlighted(Cards(window)[5]));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void UpOnTheFirstSection_StaysPut() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            window.NavigateUp();

            Assert.Equal(0, window.HighlightedSection);
            Assert.True(IsHighlighted(Cards(window)[0]));
        }
        finally { window.Close(); }
    });

    private static IEnumerable<KeyBinding> Bindings(HelpWindow window) => window.InputBindings.OfType<KeyBinding>();

    [Fact]
    public void TheDefaultKeys_AreAltDownAndAltUp_AndDriveTheHighlight() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            var down = Assert.Single(Bindings(window), b => b.Key == Key.Down && b.Modifiers == ModifierKeys.Alt);
            var up = Assert.Single(Bindings(window), b => b.Key == Key.Up && b.Modifiers == ModifierKeys.Alt);

            down.Command.Execute(null);
            Assert.Equal(1, window.HighlightedSection);
            up.Command.Execute(null);
            Assert.Equal(0, window.HighlightedSection);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ARebindOfSwitchWorkspace_MovesTheHelpKeysToo() => Ui.Run(() =>
    {
        var keys = AppConfig.DefaultKeybindings();
        keys["switchWorkspaceDown"] = "Ctrl+J";
        keys["switchWorkspaceUp"] = "Ctrl+K";
        var window = Show(keys);
        try
        {
            Assert.Contains(Bindings(window), b => b.Key == Key.J && b.Modifiers == ModifierKeys.Control);
            Assert.Contains(Bindings(window), b => b.Key == Key.K && b.Modifiers == ModifierKeys.Control);
            Assert.DoesNotContain(Bindings(window), b => b.Key == Key.Down && b.Modifiers == ModifierKeys.Alt);
            Assert.Contains("Ctrl + J and Ctrl + K", ((TextBlock)window.FindName("HintText")).Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ABadGesture_FallsBackToTheDefault() => Ui.Run(() =>
    {
        var keys = AppConfig.DefaultKeybindings();
        keys["switchWorkspaceDown"] = "not a gesture";
        var window = Show(keys);
        try
        {
            Assert.Contains(Bindings(window), b => b.Key == Key.Down && b.Modifiers == ModifierKeys.Alt);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheHintNamesTheKeys() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            string hint = ((TextBlock)window.FindName("HintText")).Text;

            Assert.Contains("Alt + Down and Alt + Up move through the sections", hint);
            Assert.Contains("Esc closes", hint);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheHighlightedSection_IsScrolledToTheMiddleOfTheList() => Ui.Run(() =>
    {
        var window = Show(height: 340);
        try
        {
            var scroll = (ScrollViewer)window.FindName("Scroll");
            Assert.True(scroll.ScrollableHeight > 0, "the sections should overflow a short window");

            for (int i = 0; i < 4; i++) window.NavigateDown();
            Ui.Settle();

            var card = Cards(window)[4];
            var top = card.TranslatePoint(new Point(0, 0), scroll).Y;
            double middle = top + card.ActualHeight / 2;
            Assert.True(scroll.VerticalOffset > 0);
            Assert.True(top < scroll.ViewportHeight && top + card.ActualHeight > 0, "the highlighted section must be in view");
            Assert.InRange(middle, 0, scroll.ViewportHeight); // its middle is on screen, not just an edge
        }
        finally { window.Close(); }
    });

    // ----- Keyboard focus: the keys only reach the window while something inside it has focus -----

    private static IInputElement? Focused(HelpWindow window) => FocusManager.GetFocusedElement(window);

    [Fact]
    public void FocusIsNeverLeftEmpty_SoTheNavigationKeysKeepReachingTheWindow() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            Assert.Same(window.FindName("Root"), Focused(window)); // at once, before any key or click

            for (int i = 0; i < 6; i++)
            {
                window.NavigateDown();
                Ui.Settle();
                Assert.NotNull(Focused(window));
            }

            Assert.Same(window.FindName("CloseButton"), Focused(window)); // Close highlighted: Enter presses it

            window.NavigateUp();
            Ui.Settle();
            Assert.Same(window.FindName("Root"), Focused(window)); // and back on a section, still not empty
        }
        finally { window.Close(); }
    });

    [Fact]
    public void MovingSeveralTimes_KeepsWorking_NotJustTheFirstPress() => Ui.Run(() =>
    {
        var window = Show();
        try
        {
            var down = Assert.Single(Bindings(window), b => b.Key == Key.Down && b.Modifiers == ModifierKeys.Alt);
            var up = Assert.Single(Bindings(window), b => b.Key == Key.Up && b.Modifiers == ModifierKeys.Alt);

            down.Command.Execute(null);
            Assert.NotNull(Focused(window));
            down.Command.Execute(null);
            down.Command.Execute(null);
            Assert.Equal(3, window.HighlightedSection);
            up.Command.Execute(null);
            up.Command.Execute(null);
            Assert.Equal(1, window.HighlightedSection);
            Assert.NotNull(Focused(window));
        }
        finally { window.Close(); }
    });
}
