using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tidy;

public sealed record TidyPermissionResult(bool IsAllowed, string Explanation);

/// <summary>
/// Gives and takes back permission for DeskAI to tidy one connected folder.
/// </summary>
/// <remarks>
/// Granting is only ever called after the page has shown a dialog naming the folder and what
/// tidying may do. The folder is re-checked at that moment rather than trusted from when it
/// was connected: it may have been replaced by a link, moved, or become protected since.
/// Taking the permission back needs no confirmation; that is never the dangerous direction.
/// </remarks>
public sealed class TidyPermissionService(
    IAuthorizedRootRepository roots,
    IReadOnlyFolderService folders,
    IClock clock)
{
    public async Task<TidyPermissionResult> AllowAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new(false, "That folder is no longer connected.");
        }

        if (!RootCapabilities.CanReadMetadata(root))
        {
            return new(false, "That folder was not connected in a way that lets DeskAI tidy it.");
        }

        var problem = await folders.CheckStillSafeAsync(root, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return new(false, problem);
        }

        await roots.AllowTidyAsync(rootId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return new(true,
            $"DeskAI may now tidy {root.DisplayName}. It will only move loose files into folders inside it, and it never deletes anything.");
    }

    public async Task<TidyPermissionResult> StopAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await roots.StopTidyAsync(rootId, cancellationToken).ConfigureAwait(false);
        return new(true, "DeskAI can no longer tidy this folder. It is still connected for searching.");
    }
}
