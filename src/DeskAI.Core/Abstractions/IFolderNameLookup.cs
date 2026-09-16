using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

/// <summary>What, if anything, sits directly inside a connected folder under a given name.</summary>
public enum FolderEntryKind
{
    Missing,
    Folder,
    File,
    Link,
}

/// <param name="Name">The name as it is on disk when something is there, else the name asked about.</param>
public sealed record FolderEntryPresence(string Name, FolderEntryKind Kind);

/// <summary>
/// Answers, for a handful of names, whether a folder, a file, or a link already sits directly
/// inside a connected folder.
/// </summary>
/// <remarks>
/// Deliberately narrow: names and kinds only, one level, no contents, no recursion, and
/// nothing changed. It exists so folder templates can say "already there" honestly without
/// being handed a scanner. It answers only for a folder DeskAI may read.
/// </remarks>
public interface IFolderNameLookup
{
    /// <summary>At most eight names are asked about at a time.</summary>
    int MaxNames => 8;

    /// <exception cref="InvalidOperationException">The folder may not be read, with a plain reason.</exception>
    /// <exception cref="IOException">Windows could not list the folder.</exception>
    Task<IReadOnlyList<FolderEntryPresence>> LookAsync(
        AuthorizedRoot root,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default);
}
