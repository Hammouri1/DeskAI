namespace DeskAI.Core.Templates;

/// <summary>
/// A named set of empty folders to make directly inside one connected folder.
/// </summary>
/// <remarks>
/// A template only ever makes empty folders, one level deep. It never moves anything into
/// them; filling them is still Tidy, with its own preview and approval.
/// </remarks>
public sealed record FolderTemplate(string Id, string Name, IReadOnlyList<string> Folders)
{
    /// <summary>The ID of the template a person types themselves.</summary>
    public const string OwnId = "own";

    /// <summary>The folder names on one line, for a card.</summary>
    public string FolderList => string.Join(", ", Folders);

    /// <summary>The template made of names a person typed, after they passed <see cref="FolderNameCheck"/>.</summary>
    public static FolderTemplate Own(IReadOnlyList<string> folders) => new(OwnId, "Your own folders", folders);
}

/// <summary>
/// The fixed folder templates, one per starter pack.
/// </summary>
/// <remarks>
/// Held in code rather than in a data file for the same reason as the starter packs (ADR 0026):
/// nothing anyone can edit to make DeskAI create something else, and a test can run every
/// name through the real path rules. Each list matches its pack's rule destinations, so a
/// pack's rules and its template fit together.
/// </remarks>
public static class FolderTemplateCatalog
{
    /// <summary>The most folders any template, typed or built in, may make at once.</summary>
    public const int MaxFolders = 8;

    public static IReadOnlyList<FolderTemplate> All { get; } =
    [
        new("student", "Student", ["Assignments", "Slides", "Screenshots", "Notes"]),
        new("developer", "Developer", ["Projects", "Installers", "Archives"]),
        new("gaming", "Gaming", ["Clips", "Screenshots", "Installers"]),
        new("productivity", "Productivity", ["Invoices", "Spreadsheets", "Documents"]),
        new("minimal", "Minimal", ["Screenshots", "Installers"]),
    ];

    public static FolderTemplate? Find(string id) =>
        All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.Ordinal));
}
