using DeskAI.Core.Abstractions;

namespace DeskAI.Core.Roots;

/// <summary>The four folders of a person's own that DeskAI may work in.</summary>
public enum PersonalFolderKind
{
    Desktop,
    Downloads,
    Documents,
    Pictures,
}

/// <summary>One of the person's own folders, as Windows reports it.</summary>
public sealed record PersonalFolder(PersonalFolderKind Kind, string Name, string Path);

/// <summary>
/// The rule that DeskAI connects only a person's own Desktop, Downloads, Documents, and
/// Pictures, or a folder inside one of them.
/// </summary>
/// <remarks>
/// <para>
/// Decided by the owner on 2026-09-16 (ADR 0032): a person should never be able to hand DeskAI
/// a drive, a program folder, or a system location, even by picking it in a dialog. The rule
/// is deterministic code, checked when a folder is connected and again whenever a folder is
/// about to be tidied, so a folder connected before the rule cannot be changed either.
/// </para>
/// <para>
/// Containment is by path segment, so <c>Desktop2</c> is not inside <c>Desktop</c>, and case
/// does not matter, as on Windows. Where Windows knows none of the four, nothing can be
/// connected, and the reason says so.
/// </para>
/// </remarks>
public sealed class PersonalFolderPolicy(IKnownFolders knownFolders)
{
    public const string OutsideReason =
        "DeskAI only works inside your Desktop, Downloads, Documents, and Pictures. " +
        "Use Your folders on Home to connect one, or choose a folder inside one of them.";

    public const string NoneKnownReason =
        "Windows did not give DeskAI a usable location for Desktop, Downloads, Documents, or Pictures. " +
        "Check that these folders are available on this computer, then reopen DeskAI.";

    private readonly IKnownFolders _knownFolders = knownFolders;

    /// <summary>The folders Windows reports, in the order the pages show them.</summary>
    public IReadOnlyList<PersonalFolder> List()
    {
        var folders = new List<PersonalFolder>(4);
        Add(folders, PersonalFolderKind.Desktop, "Desktop", _knownFolders.Desktop);
        Add(folders, PersonalFolderKind.Downloads, "Downloads", _knownFolders.Downloads);
        Add(folders, PersonalFolderKind.Documents, "Documents", _knownFolders.Documents);
        Add(folders, PersonalFolderKind.Pictures, "Pictures", _knownFolders.Pictures);
        return folders;
    }

    public PersonalFolder? Find(PersonalFolderKind kind) => List().FirstOrDefault(folder => folder.Kind == kind);

    /// <summary>The kind whose folder <paramref name="canonicalPath"/> is or sits inside, if any.</summary>
    public PersonalFolder? Containing(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var candidate = Normalize(canonicalPath);
        return List().FirstOrDefault(folder => IsContainedBy(folder.Path, candidate));
    }

    /// <returns>Null when the folder may be connected, otherwise the reason in plain words.</returns>
    public string? Refuse(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (List().Count == 0)
        {
            return NoneKnownReason;
        }

        return Containing(canonicalPath) is null ? OutsideReason : null;
    }

    private static void Add(List<PersonalFolder> folders, PersonalFolderKind kind, string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = Normalize(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        folders.Add(new PersonalFolder(kind, name, normalized));
    }

    private static string Normalize(string path) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

    private static bool IsContainedBy(string root, string candidate)
    {
        var relative = System.IO.Path.GetRelativePath(root, candidate);
        return relative == "." ||
            (!relative.Equals("..", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !System.IO.Path.IsPathRooted(relative));
    }
}
