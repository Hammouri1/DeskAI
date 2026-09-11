namespace DeskAI.App.Help;

/// <summary>
/// Every "?" explanation in DeskAI, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Kept in one list rather than typed into each page so the rules for help text can be
/// tested: all three parts present, short enough to read at a glance, and free of the
/// technical words the main interface keeps out. Pages refer to a topic by its ID, and a test
/// scans the pages so a mistyped ID fails the tests instead of showing an empty pop-up.
/// </para>
/// <para>
/// Every sentence here must stay true. If a feature changes, its explanation changes in the
/// same commit; a help text that promises more than the app does is worse than none.
/// </para>
/// </remarks>
public static class HelpCatalog
{
    public const int MaxWhatItIsWords = 20;
    public const int MaxWhatItDoesWords = 40;
    public const int MaxWhatItNeverDoesWords = 25;

    /// <summary>Words the main interface keeps out, per the project's user-experience rules.</summary>
    public static IReadOnlyList<string> BannedWords { get; } =
    [
        "metadata", "endpoint", "provider", "schema", "sqlite", "deterministic", "authorization",
        "authorize", "telemetry", "dto", "api", "index", "token", "json", "http", "llm", "scope",
    ];

    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new("home.health", "Organization score",
            "A score out of 100 for how settled your connected folders look.",
            "It adds up two things shown below it: space that might be taken by copies, and files nobody has changed in about six months. Each part shows how much it counted.",
            "It never changes, moves, or deletes anything. It only describes."),
        new("home.duplicates", "Possible duplicates",
            "Files that are exactly the same size, so they might be copies of each other.",
            "DeskAI groups them so you can check them yourself. The same size is a hint, not proof: DeskAI has not compared what is inside them.",
            "It never deletes a copy. Nothing here changes your files."),
        new("home.storage", "Where your space is going",
            "A breakdown of how much space each kind of file uses in your connected folders.",
            "It shows totals for documents, pictures, videos, and more, plus your largest files, from what DeskAI remembered the last time it looked.",
            "It never opens, moves, or deletes files. Refresh a folder in Search to update it."),
        new("organize.tidy", "Tidying a folder",
            "A way to sort the loose files in one of your folders into neat folders inside it.",
            "Pick a folder and DeskAI suggests where each loose file belongs, like PDFs into Documents. Untick anything you want to keep where it is.",
            "Nothing moves until you press Tidy, and you can undo it. It never deletes anything or moves files out of the folder you picked."),
        new("organize.permission", "Permission to tidy",
            "Your yes, for one folder, that DeskAI may tidy it.",
            "Without it, DeskAI can only look. You can take it back at any time, and the folder stays connected for searching.",
            "It never lets DeskAI delete files, touch files in subfolders, or move anything out of the folder."),
        new("organize.suggestions", "Where suggestions come from",
            "How DeskAI decides where each file should go.",
            "It looks at the kind of file, such as PDF or photo. If one of your rules matches a file, your rule wins. If you ask, AI can suggest a place too. Each file shows which one decided.",
            "Suggestions never move anything by themselves."),
        new("organize.askAi", "Asking AI",
            "A way to get ideas from the AI you set up about where some files belong.",
            "Before anything is sent, DeskAI shows exactly what the AI would see, such as file types, and you press Send. Ideas the AI is unsure about start unticked.",
            "It never sends what is inside your files or where they are, and AI never moves anything or picks a folder outside this one."),
        new("organize.undo", "Undo",
            "A way to put back the files your last tidy moved, even after DeskAI was closed.",
            "Each file goes back only if it has not changed since and its old spot is free. Folders DeskAI made are removed once they are empty.",
            "It never replaces a file, and never removes a folder you had before or one with anything in it."),
        new("organize.interrupted", "An interrupted tidy",
            "A tidy that stopped part-way, for example because the computer turned off.",
            "DeskAI checks each file that was moving and tells you how many moved. You can put those back or keep them.",
            "It never guesses. A file it cannot be sure about is left alone for you to check."),
        new("organize.leftAlone", "Left alone",
            "Files DeskAI decided not to touch, each with its reason.",
            "For example files still downloading, changed in the last few minutes, stored online only, hidden, or of a kind DeskAI does not know.",
            "Files listed here are never moved."),
        new("search.searching", "Searching",
            "A way to find files in the folders you connected by typing what you want.",
            "Type something like \"photos from last month\" or \"documents over 10 mb\". DeskAI shows how it read your words, then lists the files that match.",
            "Searching never moves, renames, or opens a file."),
        new("search.connect", "Connecting a folder",
            "Giving DeskAI permission to look at one folder you choose.",
            "DeskAI remembers the names, sizes, and dates of the files in it so you can search them. Disconnect it at any time and DeskAI forgets everything about it.",
            "Connecting never lets DeskAI move, rename, or delete anything."),
        new("search.readInside", "Reading inside files",
            "An extra permission, for one folder, to look at the words written inside text files.",
            "Then a search also finds notes and lists that contain your words, even when the file name does not. DeskAI reads only the start of plain text files.",
            "It never opens PDFs, Word files, or photos, and never saves or sends what it reads."),
        new("search.saved", "Saved searches",
            "A search you gave a name so you can run it again with one click.",
            "Running it searches again from scratch in the folders connected right now, so the results are always up to date.",
            "A saved search is not a folder. It never moves or copies files."),
        new("automation.checking", "Checking for you",
            "DeskAI looking at your connected folders by itself while the app is open.",
            "Every so often it checks whether any file matches your rules, and tells you if something does.",
            "It never moves a file on its own. It stops when you close DeskAI."),
        new("automation.frequency", "How often",
            "How often DeskAI looks at your connected folders while it is open.",
            "Pick every 15 minutes, every hour, a few times a day, or only when you press Check now.",
            "Looking more often never makes DeskAI change anything."),
        new("automation.pause", "Pause",
            "An off switch for automatic checks.",
            "Turn it on and DeskAI stops looking by itself straight away, even in the middle of a check.",
            "Pausing never deletes your rules or your history."),
        new("automation.notifications", "Windows notifications",
            "An optional message from Windows when a check finds something.",
            "It says how many files matched. It is off unless you turn it on; while it is off, DeskAI shows a quiet note inside the app instead.",
            "It never shows file names, so nobody looking at your screen sees them."),
        new("automation.rules", "Rules",
            "Instructions you write for a tidy-up you do again and again.",
            "For example: when a file name contains \"invoice\", it belongs in Sorted. You can turn a rule off or delete it at any time.",
            "Saving a rule never moves anything by itself."),
        new("automation.practice", "Practice run",
            "A preview of exactly what your rules would do right now.",
            "It lists each file that would move and where it would go, plus any files your rules disagree about, which are left alone.",
            "A practice run never moves anything."),
        new("automation.sentence", "Writing a rule in your own words",
            "A shortcut for filling in the rule form.",
            "Type something like \"move invoices to Documents\" and DeskAI fills in the boxes below. Check them, give the rule a name, and save it.",
            "It never saves a rule for you. It only fills in the boxes."),
        new("settings.sharing", "What online AI may see",
            "Your choice of what information about your files may be sent to online AI.",
            "Switch on only what you are happy to share, such as file types. It matters only if you turn on online AI below.",
            "Private and protected files are always left out. What is written inside your files is never sent."),
        new("settings.aiChoice", "Choosing how AI works",
            "Whether DeskAI uses AI at all, and where that AI runs.",
            "Choose no AI, AI running on this computer, or an online AI service with your own key. AI only ever gives ideas.",
            "If your choice stops working, DeskAI never quietly switches to a different service."),
        new("settings.key", "Your key",
            "The password-like code an online AI service gave you, so DeskAI can use your account.",
            "Windows keeps it safe. DeskAI uses it only for the service you picked, and only when you ask for AI ideas.",
            "DeskAI never shows your key again and never sends it to a different company."),
        new("settings.dailyLimit", "Daily limit",
            "The most online AI requests DeskAI may make in one day.",
            "Once it is reached, DeskAI stops asking the AI service until tomorrow. Each time you ask AI for ideas counts as one request.",
            "DeskAI never retries by itself, so it cannot use up requests without you."),
        new("shell.scope", "What DeskAI can see",
            "A reminder of which of your folders DeskAI is allowed to look at right now.",
            "It counts the folders you connected and the files DeskAI remembers, and says if you let it read inside or tidy any of them.",
            "DeskAI never looks anywhere you have not chosen."),
    ];

    public static HelpTopic? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(topic => string.Equals(topic.Id, id, StringComparison.Ordinal));
}
