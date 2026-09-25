using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Ream.App;
using Ream.App.Services;
using Ream.App.ViewModels;
using Ream.App.Views;
using Ream.Core.Models;

namespace Ream.Tests;

/// <summary>The File tab: New / Open / Save / Save As, the auto-save switch, Clear, and how they follow the config.</summary>
public class FileRibbonTests
{
    private sealed class RecordingReamFiles(AppViewModel app, bool applyAutoSave = true) : IReamFiles
    {
        public List<string> Calls { get; } = [];

        public bool NewReam() => Record("new");
        public bool OpenReam() => Record("open");
        public bool OpenReam(string path) => Record($"openRecent:{path}");
        public bool Save() => Record("save");
        public bool SaveAs() => Record("saveAs");
        public bool ClearReam() => Record("clear");
        public bool ConfirmLeave() => true;

        public void SetAutoSave(bool on)
        {
            Calls.Add($"autoSave:{on}");
            if (applyAutoSave) app.AutoSave = on;
        }

        private bool Record(string call)
        {
            Calls.Add(call);
            return true;
        }
    }

    private static WindowFixture Docked() => new(new AppConfig { Ribbon = new RibbonConfig { AutoHide = false } }, ("W", 1));

    private static FileRibbonView FileRibbon(WindowFixture fx)
    {
        fx.Window.SelectTab(RibbonTab.File);
        Ui.Settle();
        return (FileRibbonView)fx.Window.FindName("FileRibbon");
    }

    private static T Named<T>(FileRibbonView view, string name) where T : FrameworkElement => (T)view.FindName(name);

    private static void Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Ui.Settle();
    }

    /// <summary>A real click on the switch: it toggles itself, raises Click and runs its command, exactly as the mouse would.</summary>
    private static void Click(ToggleButton toggle)
    {
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, null);
        Ui.Settle();
    }

    private static AppConfig WithBindings(Action<Dictionary<string, string>> edit)
    {
        var bindings = AppConfig.DefaultKeybindings();
        edit(bindings);
        return new AppConfig { Ribbon = new RibbonConfig { AutoHide = false }, Keybindings = bindings };
    }

    [Theory]
    [InlineData("NewButton", "new")]
    [InlineData("OpenButton", "open")]
    [InlineData("SaveButton", "save")]
    [InlineData("SaveAsButton", "saveAs")]
    [InlineData("ClearButton", "clear")]
    public void EachButton_AsksTheManagerForItsAction(string button, string call) => Ui.Run(() =>
    {
        using var fx = Docked();
        var files = new RecordingReamFiles(fx.App);
        fx.App.Files = files;
        var view = FileRibbon(fx);

        Invoke(Named<Button>(view, button));

        Assert.Equal([call], files.Calls);
    });

    [Fact]
    public void TheButtons_DoNothingWithoutAManager() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);

        foreach (var name in new[] { "NewButton", "OpenButton", "SaveButton", "SaveAsButton", "ClearButton" })
            Invoke(Named<Button>(view, name));

        Assert.True(fx.App.AutoSave);
    });

    [Fact]
    public void TheAutoSaveSwitch_FollowsTheApp_BothWays() => Ui.Run(() =>
    {
        using var fx = Docked();
        var toggle = Named<ToggleButton>(FileRibbon(fx), "AutoSaveToggle");

        Assert.True(fx.App.AutoSave);
        Assert.True(toggle.IsChecked);

        fx.App.AutoSave = false;
        Ui.Settle();
        Assert.False(toggle.IsChecked);

        fx.App.AutoSave = true;
        Ui.Settle();
        Assert.True(toggle.IsChecked);
    });

    [Fact]
    public void ClickingTheAutoSaveSwitch_AsksForTheOppositeOfWhatTheAppHas() => Ui.Run(() =>
    {
        using var fx = Docked();
        var files = new RecordingReamFiles(fx.App);
        fx.App.Files = files;
        var toggle = Named<ToggleButton>(FileRibbon(fx), "AutoSaveToggle");

        Click(toggle);
        Assert.Equal(["autoSave:False"], files.Calls);
        Assert.False(fx.App.AutoSave);
        Assert.False(toggle.IsChecked);

        Click(toggle);
        Assert.Equal(["autoSave:False", "autoSave:True"], files.Calls);
        Assert.True(fx.App.AutoSave);
        Assert.True(toggle.IsChecked);
    });

    [Fact]
    public void IfTheManagerRefusesTheChange_TheSwitchGoesBackToWhatTheAppSays() => Ui.Run(() =>
    {
        using var fx = Docked();
        var files = new RecordingReamFiles(fx.App, applyAutoSave: false);
        fx.App.Files = files;
        var toggle = Named<ToggleButton>(FileRibbon(fx), "AutoSaveToggle");

        Click(toggle);

        Assert.Equal(["autoSave:False"], files.Calls);
        Assert.True(fx.App.AutoSave);
        Assert.True(toggle.IsChecked);
    });

    [Fact]
    public void TheTooltips_CarryTheGesturesFromTheConfig() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        var bindings = fx.App.Config.Keybindings;

        foreach (var (name, action) in new[] { ("NewButton", "newReam"), ("OpenButton", "openReam"), ("SaveButton", "save"), ("SaveAsButton", "saveAs"), ("ClearButton", "clearReam") })
        {
            var tip = (string)Named<Button>(view, name).ToolTip;
            Assert.EndsWith($"({GestureText.Pretty(bindings[action])})", tip);
        }

        Assert.StartsWith("New ream (", (string)Named<Button>(view, "NewButton").ToolTip);
    });

    [Fact]
    public void TheTooltips_FollowARebind() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        var before = (string)Named<Button>(view, "SaveButton").ToolTip;

        fx.App.Config = WithBindings(b => b["save"] = "Ctrl+Alt+K");

        var after = (string)Named<Button>(view, "SaveButton").ToolTip;
        Assert.NotEqual(before, after);
        Assert.Contains("Ctrl + Alt + K", after);
        Assert.EndsWith(GestureText.Pretty(fx.App.Config.Keybindings["newReam"]) + ")", (string)Named<Button>(view, "NewButton").ToolTip);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnboundAction_HasAPlainTooltip(bool blank) => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);

        fx.App.Config = WithBindings(b =>
        {
            if (blank) b["saveAs"] = "";
            else b.Remove("saveAs");
        });

        var tip = (string)Named<Button>(view, "SaveAsButton").ToolTip;
        Assert.DoesNotContain("(", tip);
        Assert.DoesNotContain("Unbound", tip);
        Assert.Contains("(", (string)Named<Button>(view, "SaveButton").ToolTip);
    });

    [Fact]
    public void TheAutoSaveTooltip_ExplainsTheSwitch() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);

        Assert.Equal(
            "Save changes as you make them. Off: changes wait until you save, and the title shows a * while there are unsaved changes.",
            Named<ToggleButton>(view, "AutoSaveToggle").ToolTip);
    });

    [Fact]
    public void TheClearTooltip_SaysItAsksFirst() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);

        Assert.StartsWith("Remove every workspace and note (asks first; removed notes are kept in the .trash folder)", (string)Named<Button>(view, "ClearButton").ToolTip);
    });

    [Fact]
    public void ChangingTheDataContext_StopsListeningToTheOldApp() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        var other = new AppViewModel(new AppConfig(), [new WorkspaceViewModel("X", fx.Repo)], 0, fx.Repo);
        view.DataContext = other;
        var tipAfterSwap = (string)Named<Button>(view, "SaveButton").ToolTip;

        fx.App.Config = WithBindings(b => b["save"] = "Ctrl+Alt+K");

        Assert.Equal(tipAfterSwap, (string)Named<Button>(view, "SaveButton").ToolTip);
        Assert.DoesNotContain("Ctrl + Alt + K", tipAfterSwap);

        other.Config = WithBindings(b => b["save"] = "Ctrl+Alt+J");
        Assert.Contains("Ctrl + Alt + J", (string)Named<Button>(view, "SaveButton").ToolTip);
    });

    [Fact]
    public void ThePanel_FitsEveryTile_WithoutScrolling_AndWithoutClipping() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        var panel = (FrameworkElement)fx.Window.FindName("RibbonPanel");

        Assert.True(view.ActualWidth <= 1150, $"the File ribbon is {view.ActualWidth:0} px wide");
        Assert.True(view.ActualHeight <= panel.ActualHeight, $"{view.ActualHeight:0} px of ribbon in a {panel.ActualHeight:0} px panel");

        foreach (var button in Ui.Descendants<ButtonBase>(view))
        {
            var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), panel).Y;
            Assert.True(bottom <= panel.ActualHeight, $"{button.Name} ends at {bottom:0} in a {panel.ActualHeight:0} px panel");
            Assert.True(button.ActualHeight >= 50, $"{button.Name} is only {button.ActualHeight:0} px tall");
        }

        var group = Ui.Descendants<TextBlock>(view).Where(t => t.Text is "Ream" or "Recent" or "Saving" or "Tidy up" or "Help").Where(t => Ui.Ancestor<ButtonBase>(t) is null).ToList();
        Assert.Equal(5, group.Count);
        Assert.All(group, label => Assert.True(label.TranslatePoint(new Point(0, label.ActualHeight), panel).Y <= panel.ActualHeight, label.Text));
    });

    [Fact]
    public void TheFileRibbon_FitsTheDefaultWindowWidth_WithoutScrolling() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        var scroll = (ScrollViewer)fx.Window.FindName("RibbonScroll");

        fx.Window.Width = 1200;
        Ui.Settle();

        Assert.True(view.ActualWidth <= 1150, $"the File ribbon is {view.ActualWidth:0} px wide");
        if (fx.Window.ActualWidth >= 1200) Assert.Equal(0, scroll.ScrollableWidth);
    });

    [Fact]
    public void RecentButton_IsDisabled_WithNoOtherReamsYet() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        var button = Named<Button>(view, "RecentButton");

        Assert.False(button.IsEnabled);
        Assert.Equal("No other reams yet", button.ToolTip);
        Assert.Empty(view.OtherRecentReams());
    });

    [Fact]
    public void RecentButton_IsEnabled_AndExcludesTheOpenReam_WhenThereAreOthers() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);

        fx.App.Config = fx.App.Config.WithLastReam(@"C:\Reams\Current.ream")
            .WithRecentReams([@"C:\Reams\Current.ream", @"C:\Reams\Older.ream", @"C:\Reams\Oldest.ream"]);

        var button = Named<Button>(view, "RecentButton");
        Assert.True(button.IsEnabled);
        Assert.Equal("Reams opened, saved or created recently", button.ToolTip);
        Assert.Equal([@"C:\Reams\Older.ream", @"C:\Reams\Oldest.ream"], view.OtherRecentReams());
    });

    [Fact]
    public void OpenRecentReamCommand_AsksTheManagerToOpenThatPath() => Ui.Run(() =>
    {
        using var fx = Docked();
        var files = new RecordingReamFiles(fx.App);
        fx.App.Files = files;

        fx.App.OpenRecentReamCommand.Execute(@"C:\Reams\Older.ream");

        Assert.Equal([$"openRecent:C:\\Reams\\Older.ream"], files.Calls);
    });

    [Fact]
    public void HelpAndAbout_StillRaiseTheirEvents() => Ui.Run(() =>
    {
        using var fx = Docked();
        var view = FileRibbon(fx);
        fx.Window.ShowModal = _ => { };
        int help = 0, about = 0;
        view.HelpRequested += () => help++;
        view.AboutRequested += () => about++;

        Invoke(Named<Button>(view, "HelpButton"));
        Assert.Equal((1, 0), (help, about));

        Invoke(Named<Button>(view, "AboutButton"));
        Assert.Equal((1, 1), (help, about));
    });
}
