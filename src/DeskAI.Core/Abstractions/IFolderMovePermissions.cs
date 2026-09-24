namespace DeskAI.Core.Abstractions;

/// <summary>
/// The separate yes that lets Desktop Studio move things in one connected folder, whole folders
/// included (ADR 0044). It neither needs nor grants the tidy permission.
/// </summary>
public interface IFolderMovePermissions
{
    /// <summary>
    /// Records the yes. Ignored for any folder not connected for reading, so it can never reach
    /// the practice workspace or a legacy organize folder.
    /// </summary>
    Task AllowAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Takes the yes back. The folder stays connected; tidying is not affected.</summary>
    Task StopAsync(Guid rootId, CancellationToken cancellationToken = default);
}
