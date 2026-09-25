using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Views.Buddies;

/// <summary>Makes the chosen buddy. An unknown value shows Sparky, the default.</summary>
internal static class BuddyFactory
{
    public static BuddyControl Create(SearchBuddy buddy) => buddy switch
    {
        SearchBuddy.Archie => new ArchieBuddy(),
        SearchBuddy.Pip => new PipBuddy(),
        SearchBuddy.Fetch => new FetchBuddy(),
        SearchBuddy.Inky => new InkyBuddy(),
        SearchBuddy.Mochi => new MochiBuddy(),
        SearchBuddy.Paige => new PaigeBuddy(),
        _ => new SparkyBuddy(),
    };
}
