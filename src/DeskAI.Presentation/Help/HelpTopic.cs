namespace DeskAI.App.Help;

/// <summary>
/// One "?" explanation, in the three parts every explanation has.
/// </summary>
/// <remarks>
/// "What it never does" is its own part on purpose. The question a cautious person has about
/// a file tool is rarely "what does this do" and almost always "what could it do to my files",
/// so the answer to that gets its own line instead of being buried in a paragraph.
/// </remarks>
public sealed record HelpTopic(
    string Id,
    string Title,
    string WhatItIs,
    string WhatItDoes,
    string WhatItNeverDoes)
{
    /// <summary>Shown only if a page names a topic that does not exist; a test prevents that.</summary>
    public static HelpTopic Missing { get; } = new(
        "missing",
        "Help",
        "An explanation that has not been written yet.",
        "Nothing on this page changes your files unless it says so clearly.",
        "DeskAI never moves or deletes anything without you approving it.");
}
