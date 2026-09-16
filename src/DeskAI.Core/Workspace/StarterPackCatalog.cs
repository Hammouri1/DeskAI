using DeskAI.Core.Classification;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Workspace;

/// <summary>
/// Every starter pack DeskAI offers, fixed in code.
/// </summary>
/// <remarks>
/// <para>
/// A closed list rather than files loaded at run time, for the same reason rules are a closed
/// set of types: what a pack can add is readable in one place and checked by tests, and there
/// is no file beside the app that could be edited to make a pack add something else.
/// </para>
/// <para>
/// Every phrase here is tested against the search translator, so a pack search always means
/// what its name says. If the translator reads a phrase differently, the phrase changes.
/// </para>
/// </remarks>
public static class StarterPackCatalog
{
    public static IReadOnlyList<StarterPack> All { get; } =
    [
        new(
            "student",
            "Student",
            "Coursework, slides, and screenshots",
            [
                new("Slides", "slides"),
                new("Recent documents", "documents from last month"),
                new("Screenshots", "screenshots"),
            ],
            [
                Category("Slides", FileCategory.Presentations),
                NameContains("Assignments", "assignment"),
                Category("Screenshots", FileCategory.Screenshots),
            ]),
        new(
            "developer",
            "Developer",
            "Downloads, archives, and big files",
            [
                new("Archives", "archives"),
                new("Installers", "installers"),
                new("Big files", "big files"),
            ],
            [
                Category("Installers", FileCategory.Installers),
                Category("Archives", FileCategory.Archives),
            ]),
        new(
            "gaming",
            "Gaming",
            "Clips, captures, and installers",
            [
                new("Videos", "videos"),
                new("Big videos", "big videos"),
                new("Screenshots", "screenshots"),
            ],
            [
                Category("Clips", FileCategory.Videos),
                Category("Screenshots", FileCategory.Screenshots),
                Category("Installers", FileCategory.Installers),
            ]),
        new(
            "productivity",
            "Productivity",
            "Documents, sheets, and invoices",
            [
                new("Documents", "documents"),
                new("Spreadsheets", "spreadsheets"),
                new("Presentations", "presentations"),
            ],
            [
                NameContains("Invoices", "invoice"),
                Category("Spreadsheets", FileCategory.Spreadsheets),
            ]),
        new(
            "minimal",
            "Minimal",
            "Just the two that pile up most",
            [
                new("Screenshots", "screenshots"),
                new("Installers", "installers"),
            ],
            []),
    ];

    /// <summary>Finds a pack by its exact ID, or returns null.</summary>
    public static StarterPack? Find(string? id) =>
        string.IsNullOrEmpty(id)
            ? null
            : All.FirstOrDefault(pack => string.Equals(pack.Id, id, StringComparison.Ordinal));

    // A rule is named after the folder it fills, so Automatic tasks shows the word a person
    // would have typed there.
    private static StarterPackRule Category(string destination, FileCategory category) =>
        new(destination, [new CategoryIsCondition(category)], destination);

    private static StarterPackRule NameContains(string destination, string text) =>
        new(destination, [new NameContainsCondition(text)], destination);
}
