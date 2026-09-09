using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;

namespace DeskAI.Core.Search;

/// <summary>One folder DeskAI has been allowed to remember, and how much it remembers.</summary>
public sealed record ConnectedFolder(
    Guid Id,
    string Name,
    string Path,
    int FileCount,
    DateTimeOffset? LastCheckedUtc);

/// <summary>
/// The outcome of connecting or refreshing a folder, including a refusal reason.
/// </summary>
/// <remarks>
/// A refusal is a normal result rather than an exception, because "this folder is protected"
/// is something the person needs to read, not an error to swallow.
/// </remarks>
public sealed record ConnectFolderResult(bool IsAllowed, string Explanation, ConnectedFolder? Folder);

/// <summary>
/// Connects a folder so search can find things in it, and disconnects it so search forgets.
/// </summary>
/// <remarks>
/// <para>
/// This is the missing link between authorization and search: authorizing a folder records
/// that DeskAI may look at it, but the index stays empty until something asks for a scan.
/// Both halves happen here so a folder cannot end up authorized yet unsearchable.
/// </para>
/// <para>
/// Every step still goes through the existing guards. <see cref="IReadOnlyFolderService"/>
/// applies path policy and stores the root as metadata-only, so a connected folder can
/// never become the target of a plan that moves files.
/// <see cref="IMetadataIndexService"/> re-checks policy before reading anything.
/// </para>
/// <para>
/// Disconnecting erases the remembered rows before revoking the root, so nothing survives
/// as an orphan. Files on disk are never touched by any method here.
/// </para>
/// </remarks>
public sealed class ConnectedFolderService(
    IReadOnlyFolderService folders,
    IMetadataIndexService index,
    IAuthorizedRootRepository roots)
{
    /// <summary>
    /// Bounds for the scan a connect performs. Deliberately modest: connecting should feel
    /// immediate and predictable, and a person can refresh again once they see it worked.
    /// </summary>
    public static MetadataScanOptions ScanBounds { get; } = new(maxDepth: 4, maxEntries: 2000);

    private readonly IReadOnlyFolderService _folders = folders;
    private readonly IMetadataIndexService _index = index;
    private readonly IAuthorizedRootRepository _roots = roots;

    /// <summary>Authorizes <paramref name="path"/> and remembers what is inside it.</summary>
    public async Task<ConnectFolderResult> ConnectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _folders
            .AuthorizeAndPreviewAsync(path, ScanBounds, cancellationToken)
            .ConfigureAwait(false);

        if (!authorization.IsAllowed || authorization.Root is null)
        {
            return new ConnectFolderResult(false, authorization.Explanation, null);
        }

        var update = await _index
            .RefreshAsync(authorization.Root, ScanBounds, cancellationToken)
            .ConfigureAwait(false);

        // The folder is authorized but the index refused it. Say so plainly instead of
        // reporting a successful connection that would then find nothing.
        if (!update.IsAllowed)
        {
            return new ConnectFolderResult(false, update.Explanation, null);
        }

        var folder = await DescribeAsync(authorization.Root.Id, cancellationToken).ConfigureAwait(false);
        var skipped = update.Issues.Count == 0
            ? string.Empty
            : $" {update.Issues.Count} item(s) were skipped safely.";

        return new ConnectFolderResult(
            true,
            $"Remembered {update.Changes.TotalSeen} file(s): names, sizes, and dates only.{skipped}",
            folder);
    }

    /// <summary>Rescans a folder that is already connected.</summary>
    public async Task<ConnectFolderResult> RefreshAsync(
        Guid rootId,
        CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new ConnectFolderResult(false, "That folder is no longer connected.", null);
        }

        var update = await _index.RefreshAsync(root, ScanBounds, cancellationToken).ConfigureAwait(false);
        if (!update.IsAllowed)
        {
            return new ConnectFolderResult(false, update.Explanation, null);
        }

        var folder = await DescribeAsync(rootId, cancellationToken).ConfigureAwait(false);
        return new ConnectFolderResult(true, update.Explanation, folder);
    }

    /// <summary>Lists the folders search is allowed to look in.</summary>
    public async Task<IReadOnlyList<ConnectedFolder>> ListAsync(CancellationToken cancellationToken = default)
    {
        var authorized = await _folders.ListAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        var described = new List<ConnectedFolder>(authorized.Count);
        foreach (var root in authorized)
        {
            var statistics = await _index.GetStatisticsAsync(root.Id, cancellationToken).ConfigureAwait(false);
            described.Add(new ConnectedFolder(
                root.Id,
                root.DisplayName,
                root.CanonicalPath,
                statistics.FileCount,
                statistics.LastIndexedAtUtc));
        }

        return described.AsReadOnly();
    }

    /// <summary>
    /// Forgets everything remembered about a folder and removes the authorization.
    /// </summary>
    /// <remarks>
    /// The index is cleared before the root is revoked so no remembered row can outlive the
    /// permission that justified it. Nothing on disk is changed.
    /// </remarks>
    public async Task DisconnectAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await _index.ForgetAsync(rootId, cancellationToken).ConfigureAwait(false);
        await _folders.RevokeAsync(rootId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConnectedFolder?> DescribeAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return null;
        }

        var statistics = await _index.GetStatisticsAsync(rootId, cancellationToken).ConfigureAwait(false);
        return new ConnectedFolder(
            root.Id,
            root.DisplayName,
            root.CanonicalPath,
            statistics.FileCount,
            statistics.LastIndexedAtUtc);
    }
}
