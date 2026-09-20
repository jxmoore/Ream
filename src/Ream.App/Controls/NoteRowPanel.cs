using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ream.App.Animation;
using Ream.Core.Layout;
using Ream.Core.Models;

namespace Ream.App.Controls;

/// <summary>
/// A horizontally scrolling row of note columns. The view scrolls minimally (or centers)
/// to keep the focused column visible; column width and fullscreen changes animate.
/// </summary>
public sealed class NoteRowPanel : Panel
{
    private const double Epsilon = 0.01;
    private const int FullscreenZIndex = 10;

    public static readonly DependencyProperty FocusedIndexProperty = DependencyProperty.Register(
        nameof(FocusedIndex), typeof(int), typeof(NoteRowPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsArrange, OnFocusedIndexChanged));

    // False while this row's workspace is off to the side. Focus changes made then jump instead of scrolling, so
    // arriving in the workspace does not show the row sliding sideways.
    public static readonly DependencyProperty IsCurrentWorkspaceProperty = DependencyProperty.Register(
        nameof(IsCurrentWorkspace), typeof(bool), typeof(NoteRowPanel), new PropertyMetadata(true));

    public static readonly DependencyProperty ConfigProperty = DependencyProperty.Register(
        nameof(Config), typeof(AppConfig), typeof(NoteRowPanel),
        new FrameworkPropertyMetadata(
            new AppConfig(),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset), typeof(double), typeof(NoteRowPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));

    // Target width (set from the view model) and the animated width actually laid out.
    public static readonly DependencyProperty WidthFractionProperty = DependencyProperty.RegisterAttached(
        "WidthFraction", typeof(double), typeof(NoteRowPanel),
        new PropertyMetadata(0.5, OnWidthFractionChanged));

    public static readonly DependencyProperty ActualFractionProperty = DependencyProperty.RegisterAttached(
        "ActualFraction", typeof(double), typeof(NoteRowPanel),
        new FrameworkPropertyMetadata(
            0.5,
            FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty IsFullscreenProperty = DependencyProperty.RegisterAttached(
        "IsFullscreen", typeof(bool), typeof(NoteRowPanel),
        new PropertyMetadata(false, OnIsFullscreenChanged));

    // While a column's edge is being dragged, width changes follow the pointer instead of animating.
    public static readonly DependencyProperty IsResizingProperty = DependencyProperty.RegisterAttached(
        "IsResizing", typeof(bool), typeof(NoteRowPanel), new PropertyMetadata(false));

    // Whether a column is within a screen of the viewport (in a workspace that is itself near). Views use it to
    // load a note's text only when it is about to be seen. Inherited, so the view inside the column sees it.
    // Defaults to true so a view outside any row just loads.
    public static readonly DependencyProperty IsNearProperty = DependencyProperty.RegisterAttached(
        "IsNear", typeof(bool), typeof(NoteRowPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnIsNearChanged));

    // 0 = normal column, 1 = covering the whole row viewport.
    public static readonly DependencyProperty FullscreenProgressProperty = DependencyProperty.RegisterAttached(
        "FullscreenProgress", typeof(double), typeof(NoteRowPanel),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    private double _targetOffset;
    private Size _lastSize;
    private bool _initialized;
    private bool _snapNextArrange;

    public NoteRowPanel()
    {
        ClipToBounds = true;
    }

    public int FocusedIndex
    {
        get => (int)GetValue(FocusedIndexProperty);
        set => SetValue(FocusedIndexProperty, value);
    }

    public bool IsCurrentWorkspace
    {
        get => (bool)GetValue(IsCurrentWorkspaceProperty);
        set => SetValue(IsCurrentWorkspaceProperty, value);
    }

    private static void OnFocusedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Decided now, while the workspace flag still describes where the row was when focus moved.
        var panel = (NoteRowPanel)d;
        if (!panel.IsCurrentWorkspace) panel._snapNextArrange = true;
    }

    public AppConfig Config
    {
        get => (AppConfig)GetValue(ConfigProperty);
        set => SetValue(ConfigProperty, value);
    }

    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public static double GetWidthFraction(DependencyObject d) => (double)d.GetValue(WidthFractionProperty);
    public static void SetWidthFraction(DependencyObject d, double value) => d.SetValue(WidthFractionProperty, value);
    public static double GetActualFraction(DependencyObject d) => (double)d.GetValue(ActualFractionProperty);
    public static bool GetIsFullscreen(DependencyObject d) => (bool)d.GetValue(IsFullscreenProperty);
    public static void SetIsFullscreen(DependencyObject d, bool value) => d.SetValue(IsFullscreenProperty, value);
    public static double GetFullscreenProgress(DependencyObject d) => (double)d.GetValue(FullscreenProgressProperty);
    public static bool GetIsNear(DependencyObject d) => (bool)d.GetValue(IsNearProperty);
    public static void SetIsNear(DependencyObject d, bool value) => d.SetValue(IsNearProperty, value);

    private static void OnIsNearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is INearAware aware) aware.OnNearChanged((bool)e.NewValue);
    }

    public static bool GetIsResizing(DependencyObject d) => (bool)d.GetValue(IsResizingProperty);
    public static void SetIsResizing(DependencyObject d, bool value) => d.SetValue(IsResizingProperty, value);

    private static void OnWidthFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        double to = (double)e.NewValue;

        bool follow = GetIsResizing(element);
        if (!follow && element is FrameworkElement { IsLoaded: true } && VisualTreeHelper.GetParent(element) is NoteRowPanel row)
            Motion.Animate(element, ActualFractionProperty, to, row.Config.Animations.ResizeMs, row.Config);
        else
            Motion.Snap(element, ActualFractionProperty, to);
    }

    private static void OnIsFullscreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        bool on = (bool)e.NewValue;
        double to = on ? 1 : 0;

        if (on) Panel.SetZIndex(element, FullscreenZIndex);

        if (element is FrameworkElement { IsLoaded: true } && VisualTreeHelper.GetParent(element) is NoteRowPanel row)
        {
            Motion.Animate(element, FullscreenProgressProperty, to, row.Config.Animations.ResizeMs, row.Config, () =>
            {
                if (!GetIsFullscreen(element)) Panel.SetZIndex(element, 0);
            });
        }
        else
        {
            Motion.Snap(element, FullscreenProgressProperty, to);
            if (!on) Panel.SetZIndex(element, 0);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = Finite(availableSize.Width), h = Finite(availableSize.Height);
        double gap = Config.Layout.GapPx;

        foreach (UIElement child in InternalChildren)
        {
            double normalW = RowLayout.ColumnWidth(GetActualFraction(child), w, gap);
            double normalH = Math.Max(0, h - 2 * gap);
            double p = GetFullscreenProgress(child);
            child.Measure(new Size(Lerp(normalW, w, p), Lerp(normalH, h, p)));
        }

        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        int n = children.Count;
        double w = finalSize.Width, h = finalSize.Height;
        double gap = Config.Layout.GapPx;

        var widths = new double[n];
        bool resizing = false;
        for (int i = 0; i < n; i++)
        {
            double actual = GetActualFraction(children[i]);
            widths[i] = RowLayout.ColumnWidth(actual, w, gap);
            if (Math.Abs(actual - GetWidthFraction(children[i])) > 1e-4 || GetIsResizing(children[i])) resizing = true;
        }

        var lefts = RowLayout.Lefts(widths, gap);
        double content = RowLayout.ContentWidth(widths, gap);

        double target = 0;
        if (n > 0)
        {
            int focus = Math.Clamp(FocusedIndex, 0, n - 1);
            target = RowLayout.TargetOffset(
                _targetOffset, lefts[focus], widths[focus], w, gap, content, Config.Layout.CenterFocusedColumn);
        }

        // While widths are animating the target moves every frame, so follow it directly
        // instead of starting a competing offset animation.
        bool snap = !_initialized || finalSize != _lastSize || resizing || !Config.Animations.Enabled || _snapNextArrange;
        _snapNextArrange = false;
        double offset;
        if (snap)
        {
            offset = target;
            if (Math.Abs(HorizontalOffset - target) > Epsilon)
                Motion.Snap(this, HorizontalOffsetProperty, target);
        }
        else
        {
            if (Math.Abs(target - _targetOffset) > Epsilon)
                Motion.Animate(this, HorizontalOffsetProperty, target, Config.Animations.ColumnFocusMs, Config);
            offset = HorizontalOffset;
        }

        _targetOffset = target;
        _lastSize = finalSize;
        _initialized = true;

        var full = new Rect(0, 0, w, h);
        bool workspaceNear = WorkspaceStripPanel.GetIsNearWorkspace(this);
        int focused = n == 0 ? -1 : Math.Clamp(FocusedIndex, 0, n - 1);
        for (int i = 0; i < n; i++)
        {
            var normal = new Rect(lefts[i] - offset, gap, widths[i], Math.Max(0, h - 2 * gap));
            var rect = Lerp(normal, full, GetFullscreenProgress(children[i]));
            children[i].Arrange(rect);

            // One viewport of margin either side, so scrolling reveals text that is already loaded.
            bool onScreenSoon = rect.Right > -w && rect.Left < 2 * w;
            SetIsNear(children[i], workspaceNear && (i == focused || onScreenSoon));
        }

        return finalSize;
    }

    private static double Finite(double v) => double.IsInfinity(v) ? 0 : v;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static Rect Lerp(Rect a, Rect b, double t) =>
        new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Width, b.Width, t), Lerp(a.Height, b.Height, t));
}
