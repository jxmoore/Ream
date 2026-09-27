using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ream.App.Animation;
using Ream.Core.Models;

namespace Ream.App.Controls;

/// <summary>
/// A vertical strip of workspaces. Every child fills the viewport; the strip is
/// scrolled by animating a fractional workspace index.
/// </summary>
public sealed class WorkspaceStripPanel : Panel
{
    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex), typeof(int), typeof(WorkspaceStripPanel),
        new FrameworkPropertyMetadata(0, OnCurrentIndexChanged));

    public static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register(
        nameof(ScrollOffset), typeof(double), typeof(WorkspaceStripPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));

    // Whether a workspace is within about a screen of the viewport. Inherited by the row inside it, which
    // re-arranges when it flips so its columns can start (or stop) counting as near.
    public static readonly DependencyProperty IsNearWorkspaceProperty = DependencyProperty.RegisterAttached(
        "IsNearWorkspace", typeof(bool), typeof(WorkspaceStripPanel),
        new FrameworkPropertyMetadata(
            true,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static bool GetIsNearWorkspace(DependencyObject d) => (bool)d.GetValue(IsNearWorkspaceProperty);

    public static void SetIsNearWorkspace(DependencyObject d, bool value) => d.SetValue(IsNearWorkspaceProperty, value);

    public static readonly DependencyProperty SuppressAnimationProperty = DependencyProperty.Register(
        nameof(SuppressAnimation), typeof(bool), typeof(WorkspaceStripPanel), new PropertyMetadata(false));

    public static readonly DependencyProperty ConfigProperty = DependencyProperty.Register(
        nameof(Config), typeof(AppConfig), typeof(WorkspaceStripPanel), new PropertyMetadata(new AppConfig()));

    public static readonly DependencyProperty SwitchCompletedCommandProperty = DependencyProperty.Register(
        nameof(SwitchCompletedCommand), typeof(ICommand), typeof(WorkspaceStripPanel), new PropertyMetadata(null));

    /// <summary>How many workspaces on either side of the current one count as "near" (see <see cref="IsNearWorkspaceProperty"/>),
    /// in workspace units - 1.5 by default, matching the roughly-a-screen-and-a-half a normal view ever shows. Board
    /// Zoom (MainWindow) grows this in proportion to how far zoomed out the board is, since zooming out can put many
    /// more workspaces on screen at once and every one of them needs its notes actually loaded to look like anything
    /// other than an empty card.</summary>
    public static readonly DependencyProperty NearRadiusProperty = DependencyProperty.Register(
        nameof(NearRadius), typeof(double), typeof(WorkspaceStripPanel),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double NearRadius
    {
        get => (double)GetValue(NearRadiusProperty);
        set => SetValue(NearRadiusProperty, value);
    }

    public WorkspaceStripPanel()
    {
        ClipToBounds = true;
    }

    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    /// <summary>Fractional index of the workspace currently at the top of the viewport.</summary>
    public double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    /// <summary>When true, index changes snap instead of animating (used when the list itself shifts).</summary>
    public bool SuppressAnimation
    {
        get => (bool)GetValue(SuppressAnimationProperty);
        set => SetValue(SuppressAnimationProperty, value);
    }

    public AppConfig Config
    {
        get => (AppConfig)GetValue(ConfigProperty);
        set => SetValue(ConfigProperty, value);
    }

    /// <summary>Executed when a switch animation has come to rest on the current workspace.</summary>
    public ICommand? SwitchCompletedCommand
    {
        get => (ICommand?)GetValue(SwitchCompletedCommandProperty);
        set => SetValue(SwitchCompletedCommandProperty, value);
    }

    private static void OnCurrentIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WorkspaceStripPanel)d).AnimateTo((int)e.NewValue);

    private void AnimateTo(int index)
    {
        if (SuppressAnimation || !IsLoaded)
        {
            Motion.Snap(this, ScrollOffsetProperty, index);
            return;
        }

        Motion.Animate(this, ScrollOffsetProperty, index, Config.Animations.WorkspaceSwitchMs, Config, OnSwitchAnimationCompleted);
    }

    private void OnSwitchAnimationCompleted()
    {
        // An interrupted animation must not report completion for a workspace we haven't reached.
        if (Math.Abs(ScrollOffset - CurrentIndex) < 1e-3 && SwitchCompletedCommand?.CanExecute(null) == true)
            SwitchCompletedCommand.Execute(null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Finite(availableSize);
        foreach (UIElement child in InternalChildren)
            child.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double offset = ScrollOffset;
        double radius = NearRadius;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            SetIsNearWorkspace(child, Math.Abs(i - offset) < radius);
            child.Arrange(new Rect(0, (i - offset) * finalSize.Height, finalSize.Width, finalSize.Height));
        }
        return finalSize;
    }

    private static Size Finite(Size s) =>
        new(double.IsInfinity(s.Width) ? 0 : s.Width, double.IsInfinity(s.Height) ? 0 : s.Height);
}
