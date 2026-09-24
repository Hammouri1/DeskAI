using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Search;

/// <summary>One folder DeskAI has been allowed to remember, and how much it remembers.</summary>
/// <remarks>
/// <see cref="CanReadContent"/> is carried so the folder list can state the permission a
/// person actually gave. A permission that is granted but invisible is one they cannot
/// reconsider.
/// </remarks>
public sealed record ConnectedFolder(
    Guid Id,
    string Name,
    string Path,
    int FileCount,
    DateTimeOffset? LastCheckedUtc,
    bool CanReadContent,
    bool CanTidy = false,
    bool CanReadDocuments = false,
    bool CanReadPdf = false,
    bool CanReadSlides = false,
    bool StoppedEarly = false,
    int DeepFoldersSkipped = 0);

/// <summary>How far Search looks into a connected folder (ADR 0041).</summary>
/// <remarks>
/// Wide enough for an ordinary Documents folder, still finite so a huge one cannot keep DeskAI
/// busy indefinitely. A look that reaches either bound says so, and keeps what it remembered
/// before rather than forgetting the part it did not reach. Registered once in the container,
/// so tests can substitute small bounds instead of generating twenty thousand files.
/// </remarks>
public sealed record SearchScanBounds(MetadataScanOptions Options)
{
    public static SearchScanBounds Default { get; } = new(new MetadataScanOptions(maxDepth: 8, maxEntries: 20_000));
}

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
    IAuthorizedRootRepository roots,
    SearchScanBounds? bounds = null)
{
    /// <summary>How far a look at a connected folder goes; see <see cref="SearchScanBounds"/>.</summary>
    public MetadataScanOptions ScanBounds { get; } = (bounds ?? SearchScanBounds.Default).Options;

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
        var skippedCount = update.Issues.Count(issue =>
            issue.Code is not (ScanIssueCode.EntryLimitReached or ScanIssueCode.DepthLimitReached));
        var skipped = skippedCount == 0
            ? string.Empty
            : $" {skippedCount} item(s) were skipped safely.";

        return new ConnectFolderResult(
            true,
            $"Remembered {update.Changes.TotalSeen} file(s): names, sizes, and dates only.{skipped}{DescribeLimits(update)}",
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
        return new ConnectFolderResult(true, update.Explanation + DescribeLimits(update), folder);
    }

    /// <summary>
    /// Says, in plain words, when a look could not cover the whole folder. Empty when it did.
    /// </summary>
    public string DescribeLimits(bool stoppedEarly, int deepFoldersSkipped)
    {
        var text = stoppedEarly
            ? $" DeskAI looked at the first {ScanBounds.MaxEntries:N0} items here and stopped, so some files may not show up in Search."
            : string.Empty;
        if (deepFoldersSkipped > 0)
        {
            text += deepFoldersSkipped == 1
                ? " One folder was too deep to look inside."
                : $" {deepFoldersSkipped:N0} folders were too deep to look inside.";
        }

        return text;
    }

    private string DescribeLimits(IndexUpdateResult update) =>
        DescribeLimits(update.StoppedEarly, update.DeepFoldersSkipped);

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
                statistics.LastLookedAtUtc ?? statistics.LastIndexedAtUtc,
                RootCapabilities.CanReadContent(root),
                RootCapabilities.CanTidy(root),
                RootCapabilities.CanReadDocuments(root),
                RootCapabilities.CanReadPdf(root),
                RootCapabilities.CanReadSlides(root),
                statistics.StoppedEarly,
                statistics.DeepFoldersSkipped));
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

    /// <summary>
    /// Lets DeskAI open the text files in an already-connected folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a deliberate second consent, not an extension of the first. Connecting a
    /// folder said DeskAI may remember names, sizes, and dates; it did not say DeskAI may
    /// read what is written inside. The caller must have shown the person exactly what will
    /// be read before calling this.
    /// </para>
    /// <para>
    /// Only a folder connected for reading can be upgraded. The practice workspace and any
    /// folder connected for organizing are refused, so this can never widen a permission
    /// that was granted for changing files.
    /// </para>
    /// </remarks>
    public Task<ConnectFolderResult> AllowContentAsync(
        Guid rootId,
        CancellationToken cancellationToken = default) =>
        ChangeContentPermissionAsync(
            rootId,
            RootAuthorizationScope.MetadataAndContent,
            "DeskAI can now read the words inside text files here. It still cannot move, rename, or delete anything.",
            cancellationToken);

    /// <summary>Expands a plain-text grant only after a new dialog names Word and Excel.</summary>
    public Task<ConnectFolderResult> AllowDocumentsAsync(
        Guid rootId,
        CancellationToken cancellationToken = default) =>
        ChangeContentPermissionAsync(
            rootId,
            RootAuthorizationScope.MetadataAndDocuments,
            "DeskAI can now read notes and modern Word and Excel files here. Nothing read is saved or sent.",
            cancellationToken);

    /// <summary>A separate PDF grant, offered only after the document grant.</summary>
    public async Task<ConnectFolderResult> AllowPdfAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadDocuments(root))
        {
            return new ConnectFolderResult(false, "Allow document reading first.", null);
        }

        var scope = RootCapabilities.CanReadSlides(root)
            ? RootAuthorizationScope.MetadataDocumentsPdfAndSlides
            : RootAuthorizationScope.MetadataDocumentsAndPdf;
        return await ChangeContentPermissionAsync(rootId, scope,
            "DeskAI can now search text in PDFs here, on this computer. Scanned pages remain unreadable.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Withdraws PDF reading while retaining the earlier document grant.</summary>
    public async Task<ConnectFolderResult> StopPdfAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadPdf(root))
        {
            return new ConnectFolderResult(false, "PDF reading is not allowed in this folder.", null);
        }

        var scope = RootCapabilities.CanReadSlides(root)
            ? RootAuthorizationScope.MetadataDocumentsAndSlides
            : RootAuthorizationScope.MetadataAndDocuments;
        return await ChangeContentPermissionAsync(rootId, scope,
            "DeskAI will no longer read PDFs here. Other allowed text reading remains available.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Slides have their own grant; older document and PDF grants remain unchanged.</summary>
    public async Task<ConnectFolderResult> AllowSlidesAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadDocuments(root))
        {
            return new ConnectFolderResult(false, "Allow document reading first.", null);
        }

        var scope = RootCapabilities.CanReadPdf(root)
            ? RootAuthorizationScope.MetadataDocumentsPdfAndSlides
            : RootAuthorizationScope.MetadataDocumentsAndSlides;
        return await ChangeContentPermissionAsync(rootId, scope,
            "DeskAI can now search text on modern PowerPoint slides here, on this computer. Slide pictures stay closed.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Withdraws slide reading while keeping any independent PDF grant.</summary>
    public async Task<ConnectFolderResult> StopSlidesAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadSlides(root))
        {
            return new ConnectFolderResult(false, "PowerPoint reading is not allowed in this folder.", null);
        }

        var scope = RootCapabilities.CanReadPdf(root)
            ? RootAuthorizationScope.MetadataDocumentsAndPdf
            : RootAuthorizationScope.MetadataAndDocuments;
        return await ChangeContentPermissionAsync(rootId, scope,
            "DeskAI will no longer read PowerPoint slides here. Other allowed text reading remains available.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes back permission to read inside the files of a folder.
    /// </summary>
    /// <remarks>
    /// Nothing extracted is stored anywhere, so withdrawing consent leaves nothing behind to
    /// delete. The folder stays connected for names, sizes, and dates.
    /// </remarks>
    public Task<ConnectFolderResult> StopContentAsync(
        Guid rootId,
        CancellationToken cancellationToken = default) =>
        ChangeContentPermissionAsync(
            rootId,
            RootAuthorizationScope.MetadataOnly,
            "DeskAI can no longer read inside these files. It still remembers names, sizes, and dates.",
            cancellationToken);

    private async Task<ConnectFolderResult> ChangeContentPermissionAsync(
        Guid rootId,
        RootAuthorizationScope scope,
        string explanation,
        CancellationToken cancellationToken)
    {
        var root = await _roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return new ConnectFolderResult(false, "That folder is no longer connected.", null);
        }

        // Only the two reading scopes may be swapped between. A folder that can be changed
        // is not a folder whose reading permission this method is entitled to touch.
        if (root.AuthorizationScope is not (RootAuthorizationScope.MetadataOnly
            or RootAuthorizationScope.MetadataAndContent
            or RootAuthorizationScope.MetadataAndDocuments
            or RootAuthorizationScope.MetadataDocumentsAndPdf
            or RootAuthorizationScope.MetadataDocumentsAndSlides
            or RootAuthorizationScope.MetadataDocumentsPdfAndSlides))
        {
            return new ConnectFolderResult(false, "That folder was not connected for reading.", null);
        }

        await _roots
            .SaveAsync(
                AuthorizedRoot.Create(root.Id, root.CanonicalPath, root.DisplayName, root.Permission, scope),
                cancellationToken)
            .ConfigureAwait(false);

        var folder = await DescribeAsync(rootId, cancellationToken).ConfigureAwait(false);
        return new ConnectFolderResult(true, explanation, folder);
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
            statistics.LastLookedAtUtc ?? statistics.LastIndexedAtUtc,
            RootCapabilities.CanReadContent(root),
            RootCapabilities.CanTidy(root),
            RootCapabilities.CanReadDocuments(root),
            RootCapabilities.CanReadPdf(root),
            RootCapabilities.CanReadSlides(root),
            statistics.StoppedEarly,
            statistics.DeepFoldersSkipped);
    }
}
