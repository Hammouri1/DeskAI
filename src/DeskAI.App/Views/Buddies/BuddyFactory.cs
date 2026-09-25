using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Views.Buddies;

/// <summary>Makes the chosen buddy. A buddy not drawn yet shows Sparky.</summary>
internal static class BuddyFactory
{
    public static BuddyControl Create(SearchBuddy buddy) => buddy switch
    {
        _ => new SparkyBuddy(),
    };
}
