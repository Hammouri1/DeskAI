using DeskAI.Core.Studio;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Keeps one Find groups board per connected folder (ADR 0042). The board goes with the folder:
/// disconnecting it or Start fresh erases it.
/// </summary>
public interface IDesktopGroupRepository
{
    /// <summary>The saved board, or null when there is none or it could not be read.</summary>
    Task<DesktopGroupBoard?> LoadAsync(Guid rootId, CancellationToken cancellationToken = default);

    Task SaveAsync(DesktopGroupBoard board, CancellationToken cancellationToken = default);
}
