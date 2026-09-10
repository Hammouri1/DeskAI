using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

public interface IAuthorizedRootRepository
{
    Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default);

    Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the person allowed DeskAI to tidy this folder. Ignored for any folder not
    /// connected for reading, so this can never add a permission to the practice workspace.
    /// </summary>
    Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Takes the tidy permission back. The folder stays connected.</summary>
    Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default);
}
