using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ream.App.Services;
using Ream.App.Views;

namespace Ream.Tests;

/// <summary>Ream's own question box: the shared drawn frame, the message, and buttons that answer. Never ShowDialog in a test.</summary>
public class PromptWindowTests
{
    private static PromptWindow Show(string title, string message, PromptIcon icon, params PromptButton[] buttons)
    {
        var window = new PromptWindow(title, message, icon, buttons)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        Ui.Settle();
        return window;
    }

    private static PromptWindow ShowYesNo() => Show("Ream", "Sure?", PromptIcon.Question,
        new PromptButton("_Yes", "yes", IsDefault: true),
        new PromptButton("_No", "no"),
        new PromptButton("Cancel", "cancel", IsCancel: true));

    private static List<Button> Buttons(Window window) =>
        Ui.Descendants<Button>(window).Where(b => b.Name.StartsWith("PromptButton")).ToList();

    private static T Part<T>(Window window, string name) where T : class
    {
        window.ApplyTemplate();
        return (T)window.Template.FindName(name, window);
    }

    private static void Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Ui.Settle();
    }

    private static bool Highlighted(Button button) =>
        button.BorderThickness.Left == 2 && ((SolidColorBrush)button.BorderBrush).Color == Themes.Brush("FocusBorderBrush");

    [Fact]
    public void TheFrameIsTheSharedDrawnTitleBar_WithTheGivenTitle() => Ui.Run(() =>
    {
        var window = Show("Delete it?", "Really?", PromptIcon.Warning, new PromptButton("OK", true));
        try
        {
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(32, Part<FrameworkElement>(window, "PART_TitleBar").ActualHeight);
            Assert.Equal("Delete it?", Part<TextBlock>(window, "PART_TitleText").Text);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.False(window.ShowInTaskbar);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheMessageIsShown() => Ui.Run(() =>
    {
        var window = Show("Ream", "Save changes to \"Foo\"?\nSecond line.", PromptIcon.Question, new PromptButton("OK", true));
        try
        {
            Assert.Equal("Save changes to \"Foo\"?\nSecond line.", ((TextBlock)window.FindName("MessageText")).Text);
            Assert.Equal(460, window.ActualWidth);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Question", "", "AccentBrush")]
    [InlineData("Warning", "", "AccentBrush")]
    [InlineData("Error", "", "ErrorBrush")]
    [InlineData("Info", "", "AccentBrush")]
    public void EachIconHasItsGlyph_InItsThemeColor(string icon, string glyph, string brush) => Ui.Run(() =>
    {
        var window = Show("Ream", "x", Enum.Parse<PromptIcon>(icon), new PromptButton("OK", true));
        try
        {
            var text = (TextBlock)window.FindName("GlyphText");
            Assert.Equal(glyph, text.Text);
            Assert.Equal(Themes.Brush(brush), ((SolidColorBrush)text.Foreground).Color);
            Assert.Contains("Segoe Fluent Icons", text.FontFamily.Source);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheButtonsAreRealButtons_InTheGivenOrder_WithAccessKeys() => Ui.Run(() =>
    {
        var window = Show("Ream", "x", PromptIcon.Question,
            new PromptButton("_Save", 1, IsDefault: true),
            new PromptButton("Do_n't save", 2),
            new PromptButton("Cancel", 3, IsCancel: true));
        try
        {
            var buttons = Buttons(window);

            Assert.Equal(["_Save", "Do_n't save", "Cancel"], buttons.Select(b => b.Content));
            Assert.Equal(["PromptButton0", "PromptButton1", "PromptButton2"], buttons.Select(b => b.Name));
            var xs = buttons.Select(b => b.TranslatePoint(new Point(0, 0), window).X).ToList();
            Assert.Equal(xs.OrderBy(x => x), xs);
            Assert.Contains(Ui.Descendants<AccessText>(window), a => a.Text == "_Save");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void OnlyTheDefaultButtonShowsTheHighlightedBorder_AndIsTheEnterAndEscButton() => Ui.Run(() =>
    {
        var window = ShowYesNo();
        try
        {
            var buttons = Buttons(window);

            Assert.True(Highlighted(buttons[0]));
            Assert.False(Highlighted(buttons[1]));
            Assert.False(Highlighted(buttons[2]));
            Assert.Equal([true, false, false], buttons.Select(b => b.IsDefault));
            Assert.Equal([false, false, true], buttons.Select(b => b.IsCancel));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void WithNoDefaultMarked_TheFirstButtonIsTheDefault() => Ui.Run(() =>
    {
        var window = Show("Ream", "x", PromptIcon.Info, new PromptButton("One", 1), new PromptButton("Two", 2));
        try
        {
            var buttons = Buttons(window);

            Assert.True(Highlighted(buttons[0]));
            Assert.False(Highlighted(buttons[1]));
            Assert.Equal([true, false], buttons.Select(b => b.IsDefault));
            Assert.Same(buttons[0], FocusManager.GetFocusedElement(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void WithTwoDefaultsMarked_OnlyTheFirstCounts() => Ui.Run(() =>
    {
        var window = Show("Ream", "x", PromptIcon.Info, new PromptButton("One", 1), new PromptButton("Two", 2, IsDefault: true), new PromptButton("Three", 3, IsDefault: true));
        try
        {
            Assert.Equal([false, true, false], Buttons(window).Select(Highlighted));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheDefaultButtonHasKeyboardFocus_WhenTheWindowOpens() => Ui.Run(() =>
    {
        var window = Show("Ream", "x", PromptIcon.Question,
            new PromptButton("A", 1), new PromptButton("B", 2, IsDefault: true), new PromptButton("C", 3));
        try
        {
            Assert.Same(Buttons(window)[1], FocusManager.GetFocusedElement(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void LeftAndRightMoveFocusBetweenButtons_AndStopAtTheEnds() => Ui.Run(() =>
    {
        var window = ShowYesNo();
        try
        {
            var buttons = Buttons(window);

            window.MoveFocus(1);
            Assert.Same(buttons[1], FocusManager.GetFocusedElement(window));
            window.MoveFocus(1);
            window.MoveFocus(1);
            Assert.Same(buttons[2], FocusManager.GetFocusedElement(window));
            window.MoveFocus(-1);
            Assert.Same(buttons[1], FocusManager.GetFocusedElement(window));
            window.MoveFocus(-1);
            window.MoveFocus(-1);
            Assert.Same(buttons[0], FocusManager.GetFocusedElement(window));

            Assert.True(Highlighted(buttons[0])); // the highlight is the default, not the focus
            Assert.False(Highlighted(buttons[1]));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheArrowKeys_ReachTheWindow_AndAreHandled() => Ui.Run(() =>
    {
        var window = ShowYesNo();
        try
        {
            var buttons = Buttons(window);
            var right = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Right)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };

            buttons[0].RaiseEvent(right);

            Assert.True(right.Handled);
            Assert.Same(buttons[1], FocusManager.GetFocusedElement(window));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(0, "yes")]
    [InlineData(1, "no")]
    [InlineData(2, "cancel")]
    public void InvokingAButton_SetsTheResult_AndClosesTheWindow(int index, string expected) => Ui.Run(() =>
    {
        var window = ShowYesNo();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Invoke(Buttons(window)[index]);

        Assert.Equal(expected, window.Result);
        Assert.True(closed);
    });

    [Fact]
    public void TheTitleBarCloseButton_GivesTheCancelResult() => Ui.Run(() =>
    {
        var window = ShowYesNo();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Invoke(Part<Button>(window, "PART_CloseButton"));

        Assert.True(closed);
        Assert.Equal("cancel", window.Result);
    });

    [Fact]
    public void WithoutACancelButton_TheXGivesNull() => Ui.Run(() =>
    {
        var window = Show("Ream", "x", PromptIcon.Info, new PromptButton("Yes", true), new PromptButton("No", false));
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Invoke(Part<Button>(window, "PART_CloseButton"));

        Assert.True(closed);
        Assert.Null(window.Result);
    });

    [Fact]
    public void ChoosingAButton_WinsOverTheCancelResult() => Ui.Run(() =>
    {
        var window = ShowYesNo();

        Invoke(Buttons(window)[1]);

        Assert.Equal("no", window.Result);
    });

    [Fact]
    public void ALongMessage_ScrollsInsteadOfGrowingTheWindow() => Ui.Run(() =>
    {
        string message = string.Join("\n", Enumerable.Range(1, 120).Select(i => $"Line {i} of a very long message"));
        var window = Show("Ream", message, PromptIcon.Error, new PromptButton("OK", true, IsDefault: true, IsCancel: true));
        try
        {
            var scroll = (ScrollViewer)window.FindName("MessageScroll");

            Assert.True(scroll.ViewportHeight < scroll.ExtentHeight);
            Assert.True(scroll.ViewportHeight <= 300);
            Assert.True(window.ActualHeight < 700, $"window is {window.ActualHeight}px tall");
            Assert.NotEmpty(Buttons(window)); // the buttons stay in the window
            Assert.True(Buttons(window)[0].TranslatePoint(new Point(0, 0), window).Y < window.ActualHeight);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void AShortMessage_MakesAShortWindow() => Ui.Run(() =>
    {
        var window = Show("Ream", "Short.", PromptIcon.Info, new PromptButton("OK", true));
        try
        {
            Assert.True(window.ActualHeight < 250, $"window is {window.ActualHeight}px tall");
        }
        finally { window.Close(); }
    });
}

/// <summary>What Ream asks, as buttons: each question's wording, default button and cancel meaning.</summary>
public class WpfUserPromptsTests
{
    private sealed class Shown
    {
        public string Title = "";
        public string Message = "";
        public PromptIcon Icon;
        public IReadOnlyList<PromptButton> Buttons = [];
    }

    /// <summary>A prompts object whose "window" answers with the button whose label is <paramref name="pick"/> (null = closed with the x).</summary>
    private static (WpfUserPrompts Prompts, Shown Shown) Prompts(string? pick)
    {
        var shown = new Shown();
        var prompts = new WpfUserPrompts((title, message, icon, buttons) =>
        {
            shown.Title = title;
            shown.Message = message;
            shown.Icon = icon;
            shown.Buttons = buttons;
            return pick is null
                ? buttons.FirstOrDefault(b => b.IsCancel)?.Result
                : buttons.First(b => b.Label == pick).Result;
        });
        return (prompts, shown);
    }

    [Theory]
    [InlineData("_Save", "Save")]
    [InlineData("Do_n't save", "DontSave")]
    [InlineData("Cancel", "Cancel")]
    [InlineData(null, "Cancel")]
    public void SaveChanges_MapsEachButtonToItsChoice(string? pick, string expected)
    {
        var (prompts, _) = Prompts(pick);

        Assert.Equal(Enum.Parse<SaveChoice>(expected), prompts.AskSaveChanges("Notes"));
    }

    [Fact]
    public void SaveChanges_AsksAboutTheNamedReam_WithSaveAsTheDefaultAndCancelAsEsc()
    {
        var (prompts, shown) = Prompts("Cancel");

        prompts.AskSaveChanges("Notes");

        Assert.Equal("Ream", shown.Title);
        Assert.Equal("Save changes to \"Notes\"?\nYour changes will be lost if you don't save.", shown.Message);
        Assert.Equal(PromptIcon.Question, shown.Icon);
        Assert.Equal(["_Save", "Do_n't save", "Cancel"], shown.Buttons.Select(b => b.Label));
        Assert.Equal([true, false, false], shown.Buttons.Select(b => b.IsDefault));
        Assert.Equal([false, false, true], shown.Buttons.Select(b => b.IsCancel));
    }

    [Fact]
    public void Confirm_SaysYesOnlyForTheConfirmButton()
    {
        var (prompts, shown) = Prompts("Delete");

        Assert.True(prompts.Confirm("Delete note", "It will be gone.", "Delete"));

        Assert.Equal("Delete note", shown.Title);
        Assert.Equal("It will be gone.", shown.Message);
        Assert.Equal(PromptIcon.Warning, shown.Icon);
        Assert.Equal(["Delete", "Cancel"], shown.Buttons.Select(b => b.Label));
    }

    [Theory]
    [InlineData("Cancel")]
    [InlineData(null)]
    public void Confirm_SaysNo_ForCancelAndForTheX(string? pick)
    {
        var (prompts, _) = Prompts(pick);

        Assert.False(prompts.Confirm("Clear", "Everything goes.", "Clear"));
    }

    [Fact]
    public void Confirm_MakesTheSafeAnswerTheDefault_AndCancelIsEscToo()
    {
        var (prompts, shown) = Prompts("Cancel");

        prompts.Confirm("Clear", "Everything goes.", "Clear");

        var confirm = shown.Buttons[0];
        var cancel = shown.Buttons[1];
        Assert.False(confirm.IsDefault);
        Assert.False(confirm.IsCancel);
        Assert.True(cancel.IsDefault);
        Assert.True(cancel.IsCancel);
    }

    [Fact]
    public void ShowError_IsOneOkButton_ThatIsDefaultAndCancel()
    {
        var (prompts, shown) = Prompts("OK");

        prompts.ShowError("Could not save", "Disk full.");

        Assert.Equal("Could not save", shown.Title);
        Assert.Equal("Disk full.", shown.Message);
        Assert.Equal(PromptIcon.Error, shown.Icon);
        var ok = Assert.Single(shown.Buttons);
        Assert.Equal("OK", ok.Label);
        Assert.True(ok.IsDefault);
        Assert.True(ok.IsCancel);
    }

    [Fact]
    public void TheRealPrompts_HaveAPublicParameterlessConstructor_ForTheServiceContainer()
    {
        Assert.NotNull(typeof(WpfUserPrompts).GetConstructor(Type.EmptyTypes));
    }
}
