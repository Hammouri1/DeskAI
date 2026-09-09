using DeskAI.Core.Search;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Stores saved searches.
/// </summary>
/// <remarks>
/// Saved searches hold a phrase and nothing about folders, so this store can never become a
/// record of what DeskAI is allowed to reach. Authorization lives only in the authorized
/// roots, and a collection is re-scoped against those every time it runs.
/// </remarks>
public interface ISavedSearchRepository
{
    /// <summary>Adds or replaces one saved search.</summary>
    Task SaveAsync(SavedSearch collection, CancellationToken cancellationToken = default);

    /// <summary>Lists saved searches, newest first.</summary>
    Task<IReadOnlyList<SavedSearch>> ListAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid collectionId, CancellationToken cancellationToken = default);
}
