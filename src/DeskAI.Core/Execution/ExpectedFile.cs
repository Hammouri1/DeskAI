namespace DeskAI.Core.Execution;

/// <summary>
/// How a file looked when the list the person approved was made.
/// </summary>
/// <remarks>
/// Checked again right before the file moves. A file whose size or last-changed time differs
/// is not the file the person reviewed, so it is left where it is.
/// </remarks>
public sealed record ExpectedFile(long SizeBytes, DateTimeOffset ModifiedAtUtc);
