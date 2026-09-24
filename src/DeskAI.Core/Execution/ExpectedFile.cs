namespace DeskAI.Core.Execution;

/// <summary>
/// How a file or folder looked when the list the person approved was made.
/// </summary>
/// <remarks>
/// Checked again right before it moves. A file whose size or last-changed time differs, or a
/// folder whose made-at or own last-changed time differs, is not what the person reviewed, so it
/// is left where it is.
/// </remarks>
public sealed record ExpectedFile(long SizeBytes, DateTimeOffset ModifiedAtUtc)
{
    /// <summary>For a folder: when it was made. A move keeps it, so it tells the same folder from a replacement (ADR 0044). Null for a file.</summary>
    public DateTimeOffset? CreatedAtUtc { get; init; }
}
