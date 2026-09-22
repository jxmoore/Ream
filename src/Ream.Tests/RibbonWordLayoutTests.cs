using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>The Home tab is laid out like Word's Home tab; what Ream cannot do yet is placed but disabled.</summary>
public class RibbonWordLayoutTests
{
    /// <summary>The Word controls that are drawn but have nothing behind them yet.</summary>
    private static readonly string[] Placed =
    [
        "FormatPainterButton", "ChangeCaseButton", "ClearFormattingButton", "SubscriptButton", "SuperscriptButton",
        "TextEffectsButton", "MultilevelListButton", "SortButton", "ShowMarksButton", "LineSpacingButton",
        "ShadingButton", "BordersButton", "FindButton", "ReplaceButton", "SelectButton", "AddInsButton",
    ];

    private static RibbonView ShowRibbon(double width = 1400)
    {
        var ribbon = new RibbonView { HorizontalAlignment = HorizontalAlignment.Left };
        Ui.Show(ribbon, width, 140);
        return ribbon;
    }

    private static T Part<T>(FrameworkElement owner, string name) where T : class => (T)owner.FindName(name);

    private static double LeftOf(RibbonView ribbon, string name) =>
        Part<FrameworkElement>(ribbon, name).TranslatePoint(new Point(0, 0), ribbon).X;

    private static double TopOf(RibbonView ribbon, string name) =>
        Part<FrameworkElement>(ribbon, name).TranslatePoint(new Point(0, 0), ribbon).Y;

    private static double MiddleOf(RibbonView ribbon, string name) => TopOf(ribbon, name) + Part<FrameworkElement>(ribbon, name).ActualHeight / 2;

    [Fact]
    public void TheGroups_RunLeftToRight_InWordsOrder() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            string[] order = ["ClipboardGroup", "FontGroup", "ParagraphGroup", "StylesGroup", "EditingGroup", "AddInsGroup", "SizeGroup"];
            var lefts = order.Select(name => LeftOf(ribbon, name)).ToList();

            Assert.Equal(lefts.OrderBy(x => x), lefts);
            Assert.Equal(order.Length, lefts.Distinct().Count());
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void EveryGroup_HasItsLabelUnderneath() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            string[] groups = ["ClipboardGroup", "FontGroup", "ParagraphGroup", "StylesGroup", "EditingGroup", "AddInsGroup"];
            string[] labels = ["Clipboard", "Font", "Paragraph", "Styles", "Editing", "Add-ins"];

            for (int i = 0; i < groups.Length; i++)
            {
                var group = Part<StackPanel>(ribbon, groups[i]);
                var label = Ui.Descendants<TextBlock>(group).Last(t => t.Text == labels[i]); // Add-ins is a tile caption too

                // The label is under the tools: its top is lower than the top of the group's band of controls.
                Assert.True(label.TranslatePoint(new Point(0, 0), group).Y > group.ActualHeight / 2, labels[i]);
            }
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void ClipboardHasPasteBig_AndCutCopyAndFormatPainterStackedBesideIt() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            Assert.True(Part<Button>(ribbon, "PasteButton").ActualHeight > 45);

            double column = LeftOf(ribbon, "CutButton");
            Assert.True(column > LeftOf(ribbon, "PasteButton"));
            Assert.Equal(column, LeftOf(ribbon, "CopyButton"), 1);
            Assert.Equal(column, LeftOf(ribbon, "FormatPainterButton"), 1);

            Assert.True(TopOf(ribbon, "CutButton") < TopOf(ribbon, "CopyButton"));
            Assert.True(TopOf(ribbon, "CopyButton") < TopOf(ribbon, "FormatPainterButton"));
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void Font_HasTwoRows_TypefaceAndSizeOnTop_EmphasisBelow() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            double top = TopOf(ribbon, "FontBox");
            Assert.Equal(top, TopOf(ribbon, "SizeBox"), 1);
            Assert.Equal(MiddleOf(ribbon, "FontBox"), MiddleOf(ribbon, "ChangeCaseButton"), 1);

            double below = TopOf(ribbon, "BoldButton");
            Assert.True(below > top + 15);
            Assert.Equal(below, TopOf(ribbon, "StrikeButton"), 1);
            Assert.Equal(below, TopOf(ribbon, "TextColorButton"), 1);

            // Word's order in the second row: B I U abc x2 x2, then effects, highlight, font color.
            string[] row = ["BoldButton", "ItalicButton", "UnderlineButton", "StrikeButton", "SubscriptButton", "SuperscriptButton",
                "TextEffectsButton", "HighlightButton", "TextColorButton"];
            var lefts = row.Select(name => LeftOf(ribbon, name)).ToList();
            Assert.Equal(lefts.OrderBy(x => x), lefts);
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void Paragraph_HasListsAndIndentsOnTop_AlignmentBelow_InWordsOrder() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            string[] top = ["BulletsButton", "NumbersButton", "MultilevelListButton", "OutdentButton", "IndentButton", "SortButton", "ShowMarksButton"];
            string[] bottom = ["AlignLeftButton", "AlignCenterButton", "AlignRightButton", "AlignJustifyButton", "LineSpacingButton", "ShadingButton", "BordersButton"];

            foreach (var row in new[] { top, bottom })
            {
                var lefts = row.Select(name => LeftOf(ribbon, name)).ToList();
                Assert.Equal(lefts.OrderBy(x => x), lefts);
                Assert.All(row, name => Assert.Equal(TopOf(ribbon, row[0]), TopOf(ribbon, name), 4));
            }

            Assert.True(TopOf(ribbon, bottom[0]) > TopOf(ribbon, top[0]) + 15);
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void TheAlignmentIcons_AreDrawnAsLines_AndAllFourDiffer() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            var icons = new[] { "AlignLeftButton", "AlignCenterButton", "AlignRightButton", "AlignJustifyButton" }
                .Select(name => Assert.IsType<System.Windows.Shapes.Path>(Part<ToggleButton>(ribbon, name).Content))
                .ToList();

            Assert.Equal(4, icons.Select(p => p.Data.ToString()).Distinct().Count());
            Assert.All(icons, p => Assert.True(p.Data.Bounds.Width > 12));
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void TheIconsAre_TheWordOnes_NotLetters() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            // Indent / outdent and list buttons carry an icon glyph (or drawing), not "L", "C", "R", "J", "1." or a bullet character.
            foreach (var name in new[] { "OutdentButton", "IndentButton", "BulletsButton", "NumbersButton" })
            {
                var button = Part<ButtonBase>(ribbon, name);
                Assert.False(button.Content is string, name);
            }

            Assert.Equal("", ((TextBlock)Part<Button>(ribbon, "OutdentButton").Content).Text);
            Assert.Equal("", ((TextBlock)Part<Button>(ribbon, "IndentButton").Content).Text);
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void StylesAreAFramedGallery_WithItsScrollAndExpandArrowsDisabled() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            var group = Part<StackPanel>(ribbon, "StylesGroup");
            var arrows = Ui.Descendants<Button>(group).ToList();

            Assert.Equal(3, arrows.Count);
            Assert.All(arrows, a => Assert.False(a.IsEnabled));
            Assert.Contains(Ui.Descendants<TextBlock>(group), t => t.Text == "¶ Normal");
            Assert.Contains(Ui.Descendants<TextBlock>(group), t => t.Text == "Heading 1");
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    // ----- Placed but not built -----

    [Fact]
    public void ControlsWithNothingBehindThem_AreThere_ButDisabled_EvenWithAnEditorFocused() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            ribbon.Bar.IsEnabled = true; // what focusing a note editor does

            foreach (var name in Placed)
            {
                var control = Part<Control>(ribbon, name);
                Assert.True(control.IsVisible, name);
                Assert.False(control.IsEnabled, name);
            }
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void TheControlsThatWork_AreEnabledOnceAnEditorIsFocused() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            ribbon.Bar.IsEnabled = true;

            string[] working =
            [
                "PasteButton", "CutButton", "CopyButton", "FontBox", "SizeBox", "BoldButton", "ItalicButton", "UnderlineButton",
                "StrikeButton", "HighlightButton", "TextColorButton", "BulletsButton", "NumbersButton", "OutdentButton", "IndentButton",
                "AlignLeftButton", "AlignCenterButton", "AlignRightButton", "AlignJustifyButton",
                "StyleNormalButton", "StyleHeading1Button", "StyleHeading2Button", "StyleHeading3Button",
            ];
            foreach (var name in working) Assert.True(Part<Control>(ribbon, name).IsEnabled, name);
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void TheSizeResets_StillWorkWithNoEditor() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            Assert.False(ribbon.Bar.IsEnabled);
            Assert.All(new[] { "ResetNoteSizeButton", "ResetWorkspaceSizesButton", "ResetAllSizesButton" },
                name => Assert.True(Part<Button>(ribbon, name).IsEnabled, name));
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    [Fact]
    public void Buttons_ShowNoFrame_UntilTheyAreOn() => Ui.Run(() =>
    {
        var ribbon = ShowRibbon();
        try
        {
            ribbon.Bar.IsEnabled = true;
            var bold = Part<ToggleButton>(ribbon, "BoldButton");

            Border Frame() => Ui.Descendants<Border>(bold).First(b => b.Name == "Frame");
            Assert.Equal(System.Windows.Media.Colors.Transparent, ((System.Windows.Media.SolidColorBrush)Frame().Background).Color);

            bold.IsChecked = true;
            Ui.Settle();
            Assert.NotEqual(System.Windows.Media.Colors.Transparent, ((System.Windows.Media.SolidColorBrush)Frame().Background).Color);
        }
        finally { Window.GetWindow(ribbon)!.Close(); }
    });

    // ----- In the window -----

    private static WindowFixture Window1200() =>
        new(new AppConfig { Animations = new AnimationConfig { Enabled = false } }, ("W", 1));

    private static ToggleButton PinOf(WindowFixture fx) => (ToggleButton)fx.Window.FindName("PinButton");

    private static void ClickPin(WindowFixture fx)
    {
        PinOf(fx).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Ui.Settle();
    }

    [Fact]
    public void ThePin_IsAPinWhileUnpinned_AndACaretUpOncePinned_LikeWord() => Ui.Run(() =>
    {
        using var fx = Window1200();
        var pin = PinOf(fx);

        Assert.Equal("", pin.Content); // the pin
        Assert.Equal("Pin the ribbon", pin.ToolTip);

        ClickPin(fx);
        Assert.True(pin.IsChecked);
        Assert.Equal("", pin.Content); // the caret pointing up
        Assert.Equal("Collapse the ribbon", pin.ToolTip);

        ClickPin(fx);
        Assert.False(pin.IsChecked);
        Assert.Equal("", pin.Content);
        Assert.Equal("Pin the ribbon", pin.ToolTip);
    });

    [Fact]
    public void ThePin_SitsAtTheBottomRightOfThePanel_LevelWithTheGroupLabels() => Ui.Run(() =>
    {
        using var fx = Window1200();
        ClickPin(fx);

        var panel = (FrameworkElement)fx.Window.FindName("RibbonPanel");
        var pin = PinOf(fx);
        var at = pin.TranslatePoint(new Point(0, 0), panel);

        Assert.True(at.Y > panel.ActualHeight / 2, "the pin is in the lower half of the panel");
        Assert.True(at.X > panel.ActualWidth - 60, "the pin is at the right edge");
    });

    [Fact]
    public void TheWholeHomeRibbon_FitsTheDefaultWindow_WithoutScrolling() => Ui.Run(() =>
    {
        using var fx = Window1200();
        ClickPin(fx);

        var scroll = (ScrollViewer)fx.Window.FindName("RibbonScroll");
        Assert.True(scroll.ScrollableWidth < 1, $"the ribbon overflows by {scroll.ScrollableWidth:0} px");
        Assert.Equal(Visibility.Collapsed, ((UIElement)fx.Window.FindName("RibbonScrollRight")).Visibility);
    });
}
