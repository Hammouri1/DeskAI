using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class FileSearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SearchAsync_ReturnsMatchesFromEveryConnectedFolder()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        var work = roots.Add("Work");
        index.Put(study, Entry(study, 1, "holiday.png", FileCategory.Images));
        index.Put(work, Entry(work, 2, "team.png", FileCategory.Images));
        var service = new FileSearchService(roots, index);

        var outcome = await service.SearchAsync("photos", Now, TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Hits.Count);
        Assert.Equal(2, outcome.FoldersSearched);
        Assert.Contains(outcome.Hits, hit => hit.RootName == "Study");
        Assert.Contains(outcome.Hits, hit => hit.RootName == "Work");
    }

    /// <summary>
    /// Permission is a live decision. A protected folder is skipped even though the index
    /// may still hold rows for it, because a cached row cannot grant access.
    /// </summary>
    [Fact]
    public async Task SearchAsync_SkipsFoldersThatAreNotAllowed()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var allowed = roots.Add("Study");
        var restricted = roots.Add("Locked", RootAccessLevel.Restricted);
        var protectedRoot = roots.Add("System", RootAccessLevel.Protected);
        index.Put(allowed, Entry(allowed, 1, "a.png", FileCategory.Images));
        index.Put(restricted, Entry(restricted, 2, "b.png", FileCategory.Images));
        index.Put(protectedRoot, Entry(protectedRoot, 3, "c.png", FileCategory.Images));
        var service = new FileSearchService(roots, index);

        var outcome = await service.SearchAsync("photos", Now, TestContext.Current.CancellationToken);

        Assert.Equal("Study", Assert.Single(outcome.Hits).RootName);
        Assert.Equal(1, outcome.FoldersSearched);
        Assert.DoesNotContain(index.SearchedRoots, id => id == restricted || id == protectedRoot);
    }

    /// <summary>
    /// A phrase nothing was understood from must not fall through to listing everything.
    /// </summary>
    [Fact]
    public async Task SearchAsync_ReturnsNothingWhenThePhraseWasNotUnderstood()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        index.Put(study, Entry(study, 1, "a.png", FileCategory.Images));
        var service = new FileSearchService(roots, index);

        var outcome = await service.SearchAsync("show me all my files", Now, TestContext.Current.CancellationToken);

        Assert.True(outcome.UnderstoodNothing);
        Assert.Empty(outcome.Hits);
        Assert.Empty(index.SearchedRoots);
    }

    [Fact]
    public async Task SearchAsync_ReportsTheScopeEvenWhenNothingMatched()
    {
        var roots = new FakeRoots();
        roots.Add("Study");
        roots.Add("Work");
        var service = new FileSearchService(roots, new FakeIndex());

        var outcome = await service.SearchAsync("photos", Now, TestContext.Current.CancellationToken);

        Assert.Empty(outcome.Hits);
        Assert.Equal(2, outcome.FoldersSearched);
        Assert.False(outcome.UnderstoodNothing);
    }

    [Fact]
    public async Task SearchAsync_StopsAtTheQueryLimitAndSaysSo()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        for (var i = 1; i <= SearchQuery.DefaultLimit + 25; i++)
        {
            index.Put(study, Entry(study, i, $"photo{i:D4}.png", FileCategory.Images));
        }

        var service = new FileSearchService(roots, index);

        var outcome = await service.SearchAsync("photos", Now, TestContext.Current.CancellationToken);

        Assert.Equal(SearchQuery.DefaultLimit, outcome.Hits.Count);
        Assert.True(outcome.ReachedLimit);
    }

    [Fact]
    public async Task SearchAsync_ResultsCarryNoAbsolutePath()
    {
        var roots = new FakeRoots();
        var index = new FakeIndex();
        var study = roots.Add("Study");
        index.Put(study, Entry(study, 1, @"Term 1\holiday.png", FileCategory.Images));
        var service = new FileSearchService(roots, index);

        var outcome = await service.SearchAsync("photos", Now, TestContext.Current.CancellationToken);

        var hit = Assert.Single(outcome.Hits);
        Assert.False(Path.IsPathRooted(hit.File.RelativePath));
        Assert.Equal("Study", hit.RootName);
    }

    [Fact]
    public async Task SearchAsync_KeepsTheChipsSoTheReadingStaysVisible()
    {
        var roots = new FakeRoots();
        roots.Add("Study");
        var service = new FileSearchService(roots, new FakeIndex());

        var outcome = await service.SearchAsync("photos from last month", Now, TestContext.Current.CancellationToken);

        Assert.Contains(outcome.Translation.Chips, chip => chip.Label == "Photos");
        Assert.Contains(outcome.Translation.Chips, chip => chip.Filter == QueryFilter.ChangedAfter);
    }

    private static IndexedFile Entry(Guid rootId, int seed, string relativePath, FileCategory category) => new(
        rootId,
        new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
        relativePath,
        FileKind.Image,
        category,
        1024,
        Now,
        Now,
        Now);

    private sealed class FakeRoots : IAuthorizedRootRepository
    {
        private readonly List<AuthorizedRoot> _roots = [];

        public Guid Add(string name, RootAccessLevel permission = RootAccessLevel.Allowed)
        {
            var id = Guid.NewGuid();
            _roots.Add(AuthorizedRoot.Create(
                id,
                Path.Combine(Path.GetTempPath(), "deskai-tests", name),
                name,
                permission,
                RootAuthorizationScope.MetadataOnly));
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
    }

    /// <summary>
    /// Applies only the filters this suite exercises. It records which roots were asked, so
    /// a test can prove a folder was never even consulted.
    /// </summary>
    private sealed class FakeIndex : IFileIndex
    {
        private readonly Dictionary<Guid, List<IndexedFile>> _files = [];

        public List<Guid> SearchedRoots { get; } = [];

        public void Put(Guid rootId, IndexedFile file)
        {
            if (!_files.TryGetValue(rootId, out var list))
            {
                list = [];
                _files[rootId] = list;
            }

            list.Add(file);
        }

        public Task<IReadOnlyList<IndexedFile>> SearchRootAsync(
            Guid rootId,
            SearchQuery query,
            CancellationToken cancellationToken = default)
        {
            SearchedRoots.Add(rootId);
            var matches = _files.TryGetValue(rootId, out var stored) ? stored : [];
            IReadOnlyList<IndexedFile> filtered = matches
                .Where(file => query.Categories.Count == 0 || query.Categories.Contains(file.Category))
                .Take(query.Limit)
                .ToList()
                .AsReadOnly();
            return Task.FromResult(filtered);
        }

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
