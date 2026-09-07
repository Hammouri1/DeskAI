using DeskAI.Core.Classification;

namespace DeskAI.Core.Recipes;

public sealed class FolderRecipe
{
    private readonly IReadOnlyDictionary<FileCategory, string> _destinations;

    public FolderRecipe(
        string id,
        string displayName,
        int version,
        IEnumerable<FolderRecipeEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries.ToArray();
        var duplicate = materialized.GroupBy(entry => entry.Category).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"The recipe maps {duplicate.Key} more than once.", nameof(entries));
        }

        Id = id;
        DisplayName = displayName;
        Version = version;
        _destinations = materialized.ToDictionary(entry => entry.Category, entry => entry.DestinationRelativeDirectory);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public int Version { get; }

    public string? FindDestination(FileCategory category) =>
        _destinations.GetValueOrDefault(category);
}

public sealed record FolderRecipeEntry
{
    public FolderRecipeEntry(FileCategory category, string destinationRelativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativeDirectory);
        if (Path.IsPathRooted(destinationRelativeDirectory) ||
            destinationRelativeDirectory.Contains(':') ||
            destinationRelativeDirectory.IndexOfAny(['*', '?']) >= 0)
        {
            throw new ArgumentException("Recipe destinations must be simple root-relative directories.", nameof(destinationRelativeDirectory));
        }

        var segments = destinationRelativeDirectory.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment =>
            segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new ArgumentException("Recipe destinations contain an unsafe path segment.", nameof(destinationRelativeDirectory));
        }

        Category = category;
        DestinationRelativeDirectory = string.Join(Path.DirectorySeparatorChar, segments);
    }

    public FileCategory Category { get; }
    public string DestinationRelativeDirectory { get; }
}
