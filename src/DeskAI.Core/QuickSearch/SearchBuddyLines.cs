namespace DeskAI.Core.QuickSearch;

/// <summary>
/// What each buddy says, in its own voice. Flavour only: the facts are always stated separately
/// under the box in DeskAI's usual words, so no character can blur them.
/// </summary>
/// <remarks>
/// The lines hold no noun that changes with the number ("Found 1!", "Found 3!"), so one form
/// reads right for one and for many.
/// </remarks>
public static class SearchBuddyLines
{
    /// <summary>Every line stays under 40 characters so the bubble never wraps into a paragraph.</summary>
    public const int MaxLength = 39;

    public static string Name(SearchBuddy buddy) => buddy switch
    {
        SearchBuddy.Archie => "Archie the owl",
        SearchBuddy.Pip => "Pip the robot",
        SearchBuddy.Fetch => "Fetch the fox",
        SearchBuddy.Inky => "Inky the octopus",
        SearchBuddy.Mochi => "Mochi",
        SearchBuddy.Paige => "Paige the paper ghost",
        _ => "Sparky",
    };

    public static string Line(SearchBuddy buddy, BuddyMood mood, int found = 0) => (buddy, mood) switch
    {
        (SearchBuddy.Archie, BuddyMood.Idle) => "Which file shall we find?",
        (SearchBuddy.Archie, BuddyMood.Thinking) => "Searching the shelves…",
        (SearchBuddy.Archie, BuddyMood.Found) => $"Ah, {found} in the archives.",
        (SearchBuddy.Archie, BuddyMood.Nothing) => "Nothing on my shelves.",
        (SearchBuddy.Archie, BuddyMood.Happy) => "Hoo-hoo!",
        (SearchBuddy.Pip, BuddyMood.Idle) => "Ready to scan.",
        (SearchBuddy.Pip, BuddyMood.Thinking) => "Scanning…",
        (SearchBuddy.Pip, BuddyMood.Found) => $"Scan done: {found} found.",
        (SearchBuddy.Pip, BuddyMood.Nothing) => "Scan done: no match.",
        (SearchBuddy.Pip, BuddyMood.Happy) => "Beep boop!",
        (SearchBuddy.Fetch, BuddyMood.Idle) => "Want me to fetch something?",
        (SearchBuddy.Fetch, BuddyMood.Thinking) => "Sniffing…",
        (SearchBuddy.Fetch, BuddyMood.Found) => $"Fetched {found}!",
        (SearchBuddy.Fetch, BuddyMood.Nothing) => "I sniffed everywhere…",
        (SearchBuddy.Fetch, BuddyMood.Happy) => "Wag wag!",
        (SearchBuddy.Inky, BuddyMood.Idle) => "All arms ready!",
        (SearchBuddy.Inky, BuddyMood.Thinking) => "Reaching…",
        (SearchBuddy.Inky, BuddyMood.Found) => $"Grabbed {found}!",
        (SearchBuddy.Inky, BuddyMood.Nothing) => "Nothing in reach.",
        (SearchBuddy.Inky, BuddyMood.Happy) => "Blub!",
        (SearchBuddy.Mochi, BuddyMood.Idle) => "Hello! What shall we find?",
        (SearchBuddy.Mochi, BuddyMood.Thinking) => "Hmm hmm…",
        (SearchBuddy.Mochi, BuddyMood.Found) => $"Yay, {found} found!",
        (SearchBuddy.Mochi, BuddyMood.Nothing) => "Aww, nothing yet.",
        (SearchBuddy.Mochi, BuddyMood.Happy) => "Squish!",
        (SearchBuddy.Paige, BuddyMood.Idle) => "Boo! Looking for something?",
        (SearchBuddy.Paige, BuddyMood.Thinking) => "Floating through…",
        (SearchBuddy.Paige, BuddyMood.Found) => $"Boo! Found {found}.",
        (SearchBuddy.Paige, BuddyMood.Nothing) => "Not a ghost of a match.",
        (SearchBuddy.Paige, BuddyMood.Happy) => "Hee hee!",
        (_, BuddyMood.Thinking) => "Looking…",
        (_, BuddyMood.Found) => $"Found {found}!",
        (_, BuddyMood.Nothing) => "Hmm, nothing yet.",
        (_, BuddyMood.Happy) => "Wheee!",
        _ => "Hi! What are we looking for?",
    };
}
