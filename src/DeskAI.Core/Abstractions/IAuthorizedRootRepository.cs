using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

public interface IAuthorizedRootRepository
{
    Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default);

    Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default);
}
