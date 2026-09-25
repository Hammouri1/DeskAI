using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views.Buddies;

/// <summary>
/// The base of every search buddy: moves it between its moods, and holds it still when DeskAI's
/// "Let my buddy move" switch is off.
/// </summary>
/// <remarks>
/// Each buddy's XAML has two groups of visual states. "Moods" (Idle, Thinking, Found, Nothing,
/// Happy) sets the pose — eyes, mouth, glow, props — with setters only. "Motion" holds the looping
/// moves for each mood (IdleMoving, ThinkingMoving, …) and "Still", which runs nothing. Keeping
/// poses and moves apart means a still buddy still shows the right mood. A buddy is decoration:
/// it takes no focus and does nothing but move. Windows' Animation effects setting is not read:
/// the owner chose DeskAI's own switch, which sits on the card with the buddies (2026-09-25).
/// </remarks>
public partial class BuddyControl : UserControl
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(BuddyMood), typeof(BuddyControl), new PropertyMetadata(BuddyMood.Idle, OnMoodChanged));

    public BuddyControl()
    {
        Loaded += (_, _) =>
        {
            if (MotionSwitch is not null)
            {
                MotionSwitch.Changed += OnMotionChanged;
            }

            GoToMood();
        };
        Unloaded += (_, _) =>
        {
            if (MotionSwitch is not null)
            {
                MotionSwitch.Changed -= OnMotionChanged;
            }
        };
        IsTabStop = false;
    }

    public BuddyMood Mood
    {
        get => (BuddyMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>A picture rather than a character: the pose only, never the moves (the faces on My workspace).</summary>
    public bool HoldsStill { get; set; }

    /// <summary>The switch this buddy follows. Null means a still picture.</summary>
    public BuddyMotion? MotionSwitch { get; set; }

    private static void OnMoodChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BuddyControl)sender).GoToMood();

    private void OnMotionChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(GoToMood);

    private void GoToMood()
    {
        var mood = Mood.ToString();
        VisualStateManager.GoToState(this, mood, useTransitions: false);
        VisualStateManager.GoToState(this, MotionSwitch is { IsOn: true } && !HoldsStill ? mood + "Moving" : "Still", useTransitions: false);
    }
}
