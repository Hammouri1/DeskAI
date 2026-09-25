namespace DeskAI.App.ViewModels;

/// <summary>How long quick search waits after a keystroke, and how long the buddy stays happy.</summary>
/// <remarks>A value, not constants, so page tests can use short waits and still test the order of things.</remarks>
public sealed record QuickSearchTiming(TimeSpan NameDelay, TimeSpan InsideDelay, TimeSpan HappyFor)
{
    public static QuickSearchTiming Default { get; } =
        new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600), TimeSpan.FromSeconds(1.5));
}
