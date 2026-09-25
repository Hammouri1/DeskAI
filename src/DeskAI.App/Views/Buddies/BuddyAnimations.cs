using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DeskAI.App.Views.Buddies;

/// <summary>
/// The buddy's entrance: it rises from below and fades in with a little overshoot (about half a
/// second). Callers skip it when "Let my buddy move" is off.
/// </summary>
/// <remarks>
/// It never changes the buddy's size. Owner-found 2026-09-26: the old entrance grew the buddy from
/// half size, and Windows drew it once at that half size and kept stretching the small drawing, so
/// buddies looked pixelated (letting go of the grow when it ended did not redraw them). Rising and
/// fading keep the drawing at full size, so it stays sharp.
/// </remarks>
internal static class BuddyAnimations
{
    public static void PopIn(UIElement element, TimeSpan delay)
    {
        var shape = new TranslateTransform { Y = 30 };
        element.RenderTransform = shape;
        var overshoot = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var story = new Storyboard { BeginTime = delay };
        story.Children.Add(To(shape, "Y", 30, 0, overshoot));
        story.Children.Add(To(element, "Opacity", 0, 1, null));

        // Hidden until the storyboard starts: a delayed storyboard does nothing until its begin
        // time, and the lowered buddy above would otherwise show for that moment.
        element.Opacity = 0;
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
