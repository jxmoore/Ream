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

    public static readonly DependencyProperty SuppressAnimationProperty = DependencyProperty.Register(
        nameof(SuppressAnimation), typeof(bool), typeof(WorkspaceStripPanel), new PropertyMetadata(false));

    public static readonly DependencyProperty ConfigProperty = DependencyProperty.Register(
        nameof(Config), typeof(AppConfig), typeof(WorkspaceStripPanel), new PropertyMetadata(new AppConfig()));

    public static readonly DependencyProperty SwitchCompletedCommandProperty = DependencyProperty.Register(
        nameof(SwitchCompletedCommand), typeof(ICommand), typeof(WorkspaceStripPanel), new PropertyMetadata(null));

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
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(0, (i - offset) * finalSize.Height, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    private static Size Finite(Size s) =>
        new(double.IsInfinity(s.Width) ? 0 : s.Width, double.IsInfinity(s.Height) ? 0 : s.Height);
}
