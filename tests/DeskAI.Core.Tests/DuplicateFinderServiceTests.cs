using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class DuplicateFinderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindAsync_GroupsFilesThatShareASize()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        index.Put(study, Entry(study, 1, "a.pdf", 50_000));
        index.Put(study, Entry(study, 2, "a copy.pdf", 50_000));
        index.Put(study, Entry(study, 3, "other.pdf", 90_000));
        var service = new DuplicateFinderService(roots, index);

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        var group = Assert.Single(report.Groups);
        Assert.Equal(50_000, group.SizeBytes);
        Assert.Equal(2, group.Count);
    }

    /// <summary>
    /// A file copied into a second connected folder appears once in each. Merging across
    /// roots before deciding what repeats is what keeps that case visible.
    /// </summary>
    [Fact]
    public async Task FindAsync_FindsACopyThatSpansTwoFolders()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        var backup = roots.Add("Backup");
        index.Put(study, Entry(study, 1, "report.pdf", 80_000));
        index.Put(backup, Entry(backup, 2, "report.pdf", 80_000));
        var service = new DuplicateFinderService(roots, index);

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        var group = Assert.Single(report.Groups);
        Assert.Equal(2, group.Count);
        Assert.Contains(group.Files, file => file.RootName == "Study");
        Assert.Contains(group.Files, file => file.RootName == "Backup");
    }

    [Fact]
    public async Task FindAsync_IgnoresSmallFilesThatCollideOnSizeConstantly()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        index.Put(study, Entry(study, 1, "a.cfg", 12));
        index.Put(study, Entry(study, 2, "b.cfg", 12));
        var service = new DuplicateFinderService(roots, index);

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        Assert.Empty(report.Groups);
    }

    [Fact]
    public async Task FindAsync_SkipsFoldersThatAreNotSearchable()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var demo = roots.Add("Demo", RootAccessLevel.Allowed, RootAuthorizationScope.ControlledDemo);
        index.Put(demo, Entry(demo, 1, "a.pdf", 50_000));
        index.Put(demo, Entry(demo, 2, "b.pdf", 50_000));
        var service = new DuplicateFinderService(roots, index);

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        Assert.Empty(report.Groups);
        Assert.Equal(0, report.FoldersIncluded);
    }

    [Fact]
    public async Task FindAsync_ReportsNothingWhenNoFolderIsConnected()
    {
        var service = new DuplicateFinderService(new FakeRoots(), new FakeIndex());

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        Assert.False(report.HasAnything);
        Assert.Equal(0, report.FoldersIncluded);
    }

    /// <summary>
    /// The saving is a ceiling on what could be freed if the copies turn out to be
    /// identical, which is why one copy of each group is excluded from it.
    /// </summary>
    [Fact]
    public async Task FindAsync_CountsReclaimableSpaceAsAllButOneCopy()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        index.Put(study, Entry(study, 1, "a.pdf", 30_000));
        index.Put(study, Entry(study, 2, "b.pdf", 30_000));
        index.Put(study, Entry(study, 3, "c.pdf", 30_000));
        var service = new DuplicateFinderService(roots, index);

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        Assert.Equal(60_000, report.ReclaimableBytes);
        Assert.Equal(3, report.TotalFiles);
    }

    /// <summary>
    /// This stage compares sizes only. Proving files identical would mean reading their
    /// bytes, which a metadata-only authorization does not permit, so the fake counts any
    /// attempt and this asserts there was none.
    /// </summary>
    [Fact]
    public async Task FindAsync_NeverReadsFileContents()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        index.Put(study, Entry(study, 1, "a.pdf", 50_000));
        index.Put(study, Entry(study, 2, "b.pdf", 50_000));
        var service = new DuplicateFinderService(roots, index);

        var report = await service.FindAsync(TestContext.Current.CancellationToken);

        Assert.True(report.HasAnything);
        Assert.Equal(0, index.ContentReads);
    }

    private static IndexedFile Entry(Guid rootId, int seed, string relativePath, long sizeBytes) => new(
        rootId,
        new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
        relativePath,
        FileKind.Document,
        FileCategory.Documents,
        sizeBytes,
        Now,
        Now,
        Now);

    private sealed class FakeRoots : IAuthorizedRootRepository
    {
        private readonly List<AuthorizedRoot> _roots = [];

        public Guid Add(
            string name,
            RootAccessLevel permission = RootAccessLevel.Allowed,
            RootAuthorizationScope scope = RootAuthorizationScope.MetadataOnly)
        {
            var id = Guid.NewGuid();
            _roots.Add(AuthorizedRoot.Create(
                id,
                Path.Combine(Path.GetTempPath(), "deskai-tests", name),
                name,
                permission,
                scope));
            return id;
        }

        public Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizedRoot>>(_roots.AsReadOnly());

        public Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roots.FirstOrDefault(root => root.Id == rootId));

        public Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIndex : IFileIndex
    {
        private readonly Dictionary<Guid, List<IndexedFile>> _files = [];

        /// <summary>Stays zero unless something tries to read a file's bytes.</summary>
        public int ContentReads { get; }

        public void Put(Guid rootId, IndexedFile file)
        {
            if (!_files.TryGetValue(rootId, out var list))
            {
                list = [];
                _files[rootId] = list;
            }

            list.Add(file);
        }

        public Task<IReadOnlyList<SizeGroup>> GetSizeCountsAsync(
            Guid rootId,
            long minimumSizeBytes,
            CancellationToken cancellationToken = default)
        {
            var files = _files.TryGetValue(rootId, out var stored) ? stored : [];
            IReadOnlyList<SizeGroup> groups = files
                .Where(file => file.SizeBytes >= minimumSizeBytes)
                .GroupBy(file => file.SizeBytes)
                .Select(group => new SizeGroup(group.Key, group.Count()))
                .ToList()
                .AsReadOnly();
            return Task.FromResult(groups);
        }

        public Task<IReadOnlyList<IndexedFile>> SearchRootAsync(
            Guid rootId,
            SearchQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            var files = _files.TryGetValue(rootId, out var stored) ? stored : [];
            IReadOnlyList<IndexedFile> matches = files
                .Where(file => query.MinSizeBytes is null || file.SizeBytes >= query.MinSizeBytes)
                .Where(file => query.MaxSizeBytes is null || file.SizeBytes <= query.MaxSizeBytes)
                .Take(query.Limit)
                .ToList()
                .AsReadOnly();
            return Task.FromResult(matches);
        }

        public Task<RootStorageSummary> SummarizeRootAsync(
            Guid rootId,
            DateTimeOffset unchangedSinceUtc,
            int largestFileCount,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IndexedFile>> ListForRootAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileIndexSyncResult> SynchronizeRootAsync(
            Guid rootId,
            IReadOnlyList<IndexedFile> files,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FileIndexStatistics> GetStatisticsAsync(
            Guid rootId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
