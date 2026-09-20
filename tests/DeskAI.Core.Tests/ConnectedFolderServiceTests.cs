using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class ConnectedFolderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);
    private static readonly string SamplePath = Path.Combine(Path.GetTempPath(), "deskai-tests", "Study");

    [Fact]
    public async Task ConnectAsync_AuthorizesThenIndexes()
    {
        var folders = new FakeFolders();
        var index = new FakeIndex();
        var service = new ConnectedFolderService(folders, index, folders);

        var result = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.Folder);
        Assert.Single(index.RefreshedRoots);
        Assert.Contains("names, sizes, and dates", result.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Authorizing records that DeskAI may look; indexing is what makes a folder findable.
    /// A folder must never end up authorized yet permanently unsearchable.
    /// </summary>
    [Fact]
    public async Task ConnectAsync_LeavesTheFolderSearchable()
    {
        var folders = new FakeFolders();
        var index = new FakeIndex();
        var service = new ConnectedFolderService(folders, index, folders);

        await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);
        var listed = await service.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, Assert.Single(listed).FileCount);
    }

    [Fact]
    public async Task ConnectAsync_ReportsARefusalWithoutIndexingAnything()
    {
        var folders = new FakeFolders { AuthorizationAllowed = false };
        var index = new FakeIndex();
        var service = new ConnectedFolderService(folders, index, folders);

        var result = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Folder);
        Assert.Empty(index.RefreshedRoots);
    }

    /// <summary>
    /// Claiming a successful connection after the index refused would promise a folder that
    /// can never return a result.
    /// </summary>
    [Fact]
    public async Task ConnectAsync_DoesNotClaimSuccessWhenIndexingWasRefused()
    {
        var folders = new FakeFolders();
        var index = new FakeIndex { IndexingAllowed = false };
        var service = new ConnectedFolderService(folders, index, folders);

        var result = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Folder);
    }

    [Fact]
    public async Task ConnectAsync_KeepsTheFolderMetadataOnlySoItCannotBeOrganized()
    {
        var folders = new FakeFolders();
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);

        await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        var stored = Assert.Single(folders.Saved);
        Assert.Equal(RootAuthorizationScope.MetadataOnly, stored.AuthorizationScope);
    }

    /// <summary>
    /// Connecting a folder says DeskAI may remember names, sizes, and dates. Reading what is
    /// written inside is a second, separate consent, so a freshly connected folder must not
    /// already have it.
    /// </summary>
    [Fact]
    public async Task ConnectAsync_DoesNotGrantPermissionToReadInsideFiles()
    {
        var folders = new FakeFolders();
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);

        var connected = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        Assert.False(connected.Folder!.CanReadContent);
    }

    [Fact]
    public async Task AllowContentAsync_GrantsReadingInsideFilesAndSaysSo()
    {
        var folders = new FakeFolders();
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);
        var connected = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        var result = await service.AllowContentAsync(
            connected.Folder!.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.True(result.Folder!.CanReadContent);
        Assert.Equal(
            RootAuthorizationScope.MetadataAndContent,
            Assert.Single(folders.Saved).AuthorizationScope);
    }

    [Fact]
    public async Task AllowDocumentsAsync_UsesANewScopeSoOldTextConsentStaysNarrow()
    {
        var folders = new FakeFolders();
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);
        var connected = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);
        await service.AllowContentAsync(connected.Folder!.Id, TestContext.Current.CancellationToken);
        Assert.False(RootCapabilities.CanReadDocuments(Assert.Single(folders.Saved)));

        var upgraded = await service.AllowDocumentsAsync(
            connected.Folder.Id, TestContext.Current.CancellationToken);

        Assert.True(upgraded.IsAllowed);
        Assert.True(upgraded.Folder!.CanReadDocuments);
        Assert.Equal(RootAuthorizationScope.MetadataAndDocuments, Assert.Single(folders.Saved).AuthorizationScope);
    }

    /// <summary>
    /// Reading inside files must stay reversible, and taking it back must leave the folder
    /// connected rather than silently disconnecting it.
    /// </summary>
    [Fact]
    public async Task StopContentAsync_TakesThePermissionBackAndKeepsTheFolderConnected()
    {
        var folders = new FakeFolders();
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);
        var connected = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);
        await service.AllowContentAsync(connected.Folder!.Id, TestContext.Current.CancellationToken);

        var result = await service.StopContentAsync(
            connected.Folder.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.False(result.Folder!.CanReadContent);
        Assert.Equal(RootAuthorizationScope.MetadataOnly, Assert.Single(folders.Saved).AuthorizationScope);
    }

    /// <summary>
    /// A folder that can be changed is not a folder whose reading permission this path may
    /// touch. Allowing it would let a consent meant for the folder list reach a scope that
    /// grants mutation.
    /// </summary>
    [Theory]
    [InlineData(RootAuthorizationScope.ControlledDemo)]
    [InlineData(RootAuthorizationScope.Organize)]
    public async Task AllowContentAsync_RefusesAFolderThatWasNotConnectedForReading(
        RootAuthorizationScope scope)
    {
        var folders = new FakeFolders();
        var root = AuthorizedRoot.Create(
            Guid.NewGuid(),
            SamplePath,
            "Practice",
            RootAccessLevel.Allowed,
            scope);
        await folders.SaveAsync(root, TestContext.Current.CancellationToken);
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);

        var result = await service.AllowContentAsync(root.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Equal(scope, Assert.Single(folders.Saved).AuthorizationScope);
    }

    [Fact]
    public async Task AllowContentAsync_ReportsAFolderThatIsNoLongerConnected()
    {
        var folders = new FakeFolders();
        var service = new ConnectedFolderService(folders, new FakeIndex(), folders);

        var result = await service.AllowContentAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Folder);
    }

    /// <summary>
    /// The index is cleared before the root is revoked, so no remembered row can outlive
    /// the permission that justified it.
    /// </summary>
    [Fact]
    public async Task DisconnectAsync_ForgetsRememberedRowsBeforeRevokingTheRoot()
    {
        var folders = new FakeFolders();
        var index = new FakeIndex();
        var service = new ConnectedFolderService(folders, index, folders);
        var connected = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);
        var rootId = connected.Folder!.Id;

        await service.DisconnectAsync(rootId, TestContext.Current.CancellationToken);

        Assert.Equal([rootId], index.ForgottenRoots);
        Assert.Equal([rootId], folders.RevokedRoots);
        Assert.Empty(await service.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_RescansAFolderThatIsStillConnected()
    {
        var folders = new FakeFolders();
        var index = new FakeIndex();
        var service = new ConnectedFolderService(folders, index, folders);
        var connected = await service.ConnectAsync(SamplePath, TestContext.Current.CancellationToken);

        var result = await service.RefreshAsync(connected.Folder!.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.Equal(2, index.RefreshedRoots.Count);
    }

    [Fact]
    public async Task RefreshAsync_SaysSoWhenTheFolderIsNoLongerConnected()
    {
        var folders = new FakeFolders();
        var index = new FakeIndex();
        var service = new ConnectedFolderService(folders, index, folders);

        var result = await service.RefreshAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Empty(index.RefreshedRoots);
    }

    private sealed class FakeFolders : IReadOnlyFolderService, IAuthorizedRootRepository
    {
        private readonly List<AuthorizedRoot> _roots = [];

        public bool AuthorizationAllowed { get; init; } = true;

        public List<AuthorizedRoot> Saved => _roots;

        public List<Guid> RevokedRoots { get; } = [];

        public Task<FolderPreviewResult> AuthorizeAndPreviewAsync(
            string selectedPath,
            MetadataScanOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!AuthorizationAllowed)
            {
                return Task.FromResult(new FolderPreviewResult(
                    false,
                    "That location is protected and was not opened.",
                    null,
                    [],
                    []));
            }

            var root = AuthorizedRoot.Create(
                Guid.NewGuid(),
                selectedPath,
                Path.GetFileName(selectedPath),
                RootAccessLevel.Allowed,
                RootAuthorizationScope.MetadataOnly);
            _roots.Add(root);
            return Task.FromResult(new FolderPreviewResult(true, "Connected.", root, [], []));
        }

        public Task<IReadOnlyList<AuthorizedRoot>> ListAuthorizedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>(_roots.AsReadOnly());

        public Task RevokeAsync(Guid rootId, CancellationToken cancellationToken = default)
        {
            RevokedRoots.Add(rootId);
            _roots.RemoveAll(root => root.Id == rootId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            ListAuthorizedAsync(cancellationToken);

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roots.FirstOrDefault(root => root.Id == rootId));

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
        {
            _roots.RemoveAll(existing => existing.Id == root.Id);
            _roots.Add(root);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            RevokeAsync(rootId, cancellationToken);

        public Task<string?> CheckStillSafeAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
        {
            var index = _roots.FindIndex(root => root.Id == rootId);
            if (index >= 0 && _roots[index].AuthorizationScope
                    is RootAuthorizationScope.MetadataOnly or RootAuthorizationScope.MetadataAndContent)
            {
                _roots[index] = _roots[index].WithTidyAllowedSince(grantedAtUtc);
            }

            return Task.CompletedTask;
        }

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default)
        {
            var index = _roots.FindIndex(root => root.Id == rootId);
            if (index >= 0)
            {
                _roots[index] = _roots[index].WithTidyAllowedSince(null);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeIndex : IMetadataIndexService
    {
        public bool IndexingAllowed { get; init; } = true;

        public List<Guid> RefreshedRoots { get; } = [];

        public List<Guid> ForgottenRoots { get; } = [];

        public Task<IndexUpdateResult> RefreshAsync(
            AuthorizedRoot root,
            MetadataScanOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(root);
            if (!IndexingAllowed)
            {
                return Task.FromResult(IndexUpdateResult.Refused("That folder is protected."));
            }

            RefreshedRoots.Add(root.Id);
            return Task.FromResult(new IndexUpdateResult(
                true,
                "Remembered what is there.",
                new FileIndexSyncResult(Added: 7, Updated: 0, Unchanged: 0, Removed: 0),
                []));
        }

        public Task<FileIndexStatistics> GetStatisticsAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RefreshedRoots.Contains(rootId) && !ForgottenRoots.Contains(rootId)
                ? new FileIndexStatistics(7, 7168, Now)
                : FileIndexStatistics.Empty);

        public Task ForgetAsync(Guid rootId, CancellationToken cancellationToken = default)
        {
            ForgottenRoots.Add(rootId);
            return Task.CompletedTask;
        }
    }
}
