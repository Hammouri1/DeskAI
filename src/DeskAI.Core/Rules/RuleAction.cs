namespace DeskAI.Core.Rules;

/// <summary>
/// What a rule proposes doing with a file it matched.
/// </summary>
/// <remarks>
/// <para>
/// A closed set of one. Moving a file into a folder is the whole vocabulary a rule has, and
/// adding to it is a deliberate decision with its own safety thinking rather than a matter
/// of writing another string. Deleting is absent and stays absent: nothing in DeskAI deletes
/// automatically.
/// </para>
/// <para>
/// An action <em>proposes</em>. It cannot move anything itself. Whatever a rule decides
/// still becomes a plan, is validated, is previewed, and waits for approval, exactly like a
/// suggestion a person asked for by hand.
/// </para>
/// </remarks>
public abstract record RuleAction
{
    public abstract string Describe();
}

/// <summary>Proposes moving the file into a folder inside the same connected folder.</summary>
public sealed record MoveToFolderAction : RuleAction
{
    public MoveToFolderAction(string destinationRelativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativeDirectory);

        // The same shape a folder recipe demands: somewhere inside the connected folder,
        // named plainly. Anything rooted, drive-qualified, wildcarded, or containing a
        // traversal segment is refused here rather than left for the path policy to catch,
        // because a rule is stored and re-run and should never hold a destination that will
        // be rejected every time it fires.
        if (Path.IsPathRooted(destinationRelativeDirectory)
            || destinationRelativeDirectory.Contains(':')
            || destinationRelativeDirectory.IndexOfAny(['*', '?']) >= 0)
        {
            throw new ArgumentException(
                "A destination must be a simple folder inside the connected folder.",
                nameof(destinationRelativeDirectory));
        }

        var segments = destinationRelativeDirectory.Split(
            ['\\', '/'],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment =>
            segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new ArgumentException(
                "A destination must be a simple folder inside the connected folder.",
                nameof(destinationRelativeDirectory));
        }

        DestinationRelativeDirectory = string.Join(Path.DirectorySeparatorChar, segments);
    }

    public string DestinationRelativeDirectory { get; }

    public override string Describe() => $"move it into {DestinationRelativeDirectory}";
}
