using DeskAI.Core.QuickSearch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views.Buddies;

/// <summary>
/// The base of every search buddy: moves it between its moods, and holds it still when Windows'
/// "Animation effects" are off.
/// </summary>
/// <remarks>
/// Each buddy's XAML has two groups of visual states. "Moods" (Idle, Thinking, Found, Nothing,
/// Happy) sets the pose — eyes, mouth, glow, props — with setters only. "Motion" holds the looping
/// moves for each mood (IdleMoving, ThinkingMoving, …) and "Still", which runs nothing. Keeping
/// poses and moves apart means a still buddy still shows the right mood. A buddy is decoration:
/// it takes no focus and does nothing but move.
/// </remarks>
public partial class BuddyControl : UserControl
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(BuddyMood), typeof(BuddyControl), new PropertyMetadata(BuddyMood.Idle, OnMoodChanged));

    private static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public BuddyControl()
    {
        Loaded += (_, _) => GoToMood();
        IsTabStop = false;
    }

    public BuddyMood Mood
    {
        get => (BuddyMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>A picture rather than a character: the pose only, never the moves (the tiles on My workspace).</summary>
    public bool HoldsStill { get; set; }

    private static void OnMoodChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BuddyControl)sender).GoToMood();

    private void GoToMood()
    {
        var mood = Mood.ToString();
        VisualStateManager.GoToState(this, mood, useTransitions: false);
        VisualStateManager.GoToState(this, Settings.AnimationsEnabled && !HoldsStill ? mood + "Moving" : "Still", useTransitions: false);
    }
}
