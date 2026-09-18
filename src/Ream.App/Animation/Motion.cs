using System.Windows;
using System.Windows.Media.Animation;
using Ream.Core.Models;

namespace Ream.App.Animation;

internal static class Motion
{
    public static IEasingFunction? CreateEasing(AnimationConfig config) => config.Easing.ToLowerInvariant() switch
    {
        "linear" => null,
        "easeinoutquad" => new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        _ => new CubicEase { EasingMode = EasingMode.EaseOut },
    };

    public static void Snap(UIElement element, DependencyProperty property, double value)
    {
        element.BeginAnimation(property, null);
        element.SetValue(property, value);
    }

    /// <summary>
    /// Animates from the property's current (possibly mid-animation) value, so a new
    /// input while a previous animation is running retargets smoothly.
    /// </summary>
    public static void Animate(
        UIElement element,
        DependencyProperty property,
        double to,
        int durationMs,
        AppConfig config,
        Action? completed = null)
    {
        if (!config.Animations.Enabled || durationMs <= 0)
        {
            Snap(element, property, to);
            if (completed is not null) element.Dispatcher.BeginInvoke(completed);
            return;
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = CreateEasing(config.Animations),
            FillBehavior = FillBehavior.HoldEnd,
        };
        if (completed is not null) animation.Completed += (_, _) => completed();
        element.BeginAnimation(property, animation);
    }
}
