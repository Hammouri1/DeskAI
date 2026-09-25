using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Views.Buddies;

/// <summary>Makes the chosen buddy. An unknown value shows Sparky, the default.</summary>
internal static class BuddyFactory
{
    /// <param name="motion">The switch it follows; null for a still picture.</param>
    public static BuddyControl Create(SearchBuddy buddy, BuddyMotion? motion)
    {
        BuddyControl control = buddy switch
        {
            SearchBuddy.Archie => new ArchieBuddy(),
            SearchBuddy.Pip => new PipBuddy(),
            SearchBuddy.Fetch => new FetchBuddy(),
            SearchBuddy.Inky => new InkyBuddy(),
            SearchBuddy.Mochi => new MochiBuddy(),
            SearchBuddy.Paige => new PaigeBuddy(),
            _ => new SparkyBuddy(),
        };
        control.MotionSwitch = motion;
        return control;
    }
}
