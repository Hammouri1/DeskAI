namespace DeskAI.Core.QuickSearch;

/// <summary>What happened when quick search was asked to open or show a file.</summary>
public sealed record LaunchResult(bool Done, string Reason)
{
    public static LaunchResult Success { get; } = new(true, string.Empty);

    public static LaunchResult Refused(string reason) => new(false, reason);
}

/// <summary>
/// Opens one file from a connected folder in its usual app, or shows it in File Explorer.
/// </summary>
/// <remarks>
/// DeskAI's only "open a file" action (ADR 0047). The implementation re-checks the live file
/// before starting anything. Only <c>QuickSearchViewModel</c> holds this; no AI type does, and a
/// test asserts both.
/// </remarks>
public interface IFileLauncher
{
    Task<LaunchResult> OpenAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);

    Task<LaunchResult> ShowInFolderAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
}
