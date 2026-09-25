using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DeskAI.App.Views.Buddies;

/// <summary>
/// The buddy's entrance: it grows up from below with a little overshoot (about half a second).
/// Callers skip it when "Let my buddy move" is off.
/// </summary>
internal static class BuddyAnimations
{
    public static void PopIn(UIElement element, TimeSpan delay)
    {
        var shape = new CompositeTransform { ScaleX = 0.5, ScaleY = 0.5, TranslateY = 30 };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = shape;
        var overshoot = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var story = new Storyboard { BeginTime = delay };
        story.Children.Add(To(shape, "ScaleX", 0.5, 1, overshoot));
        story.Children.Add(To(shape, "ScaleY", 0.5, 1, overshoot));
        story.Children.Add(To(shape, "TranslateY", 30, 0, overshoot));
        story.Children.Add(To(element, "Opacity", 0, 1, null));
        story.Begin();
    }

    internal static DoubleAnimation To(DependencyObject target, string property, double from, double to, EasingFunctionBase? easing, double seconds = 0.5)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            EasingFunction = easing,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
