using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteFileIndexTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SynchronizeRootAsync_StoresEntriesAndReportsThemAsAdded()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");

        var result = await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, @"Study\notes.txt"), Entry(rootId, 2, "budget.xlsx")],
            TestContext.Current.CancellationToken);

        Assert.Equal(new FileIndexSyncResult(Added: 2, Updated: 0, Unchanged: 0, Removed: 0), result);
        var stored = await fixture.Index.ListForRootAsync(rootId, TestContext.Current.CancellationToken);
        Assert.Equal(["budget.xlsx", @"Study\notes.txt"], stored.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SynchronizeRootAsync_IsIncrementalAndRewritesNothingWhenNothingChanged()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        IndexedFile[] pass = [Entry(rootId, 1, @"Study\notes.txt"), Entry(rootId, 2, "budget.xlsx")];
        await fixture.Index.SynchronizeRootAsync(rootId, pass, TestContext.Current.CancellationToken);

        var result = await fixture.Index.SynchronizeRootAsync(rootId, pass, TestContext.Current.CancellationToken);

        Assert.Equal(new FileIndexSyncResult(Added: 0, Updated: 0, Unchanged: 2, Removed: 0), result);
        Assert.False(result.ChangedAnything);
    }

    [Fact]
    public async Task SynchronizeRootAsync_UpdatesChangedFilesAndForgetsMissingOnes()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, @"Study\notes.txt"), Entry(rootId, 2, "budget.xlsx")],
            TestContext.Current.CancellationToken);

        var result = await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, @"Study\notes.txt", sizeBytes: 4096)],
            TestContext.Current.CancellationToken);

        Assert.Equal(new FileIndexSyncResult(Added: 0, Updated: 1, Unchanged: 0, Removed: 1), result);
        var stored = await fixture.Index.ListForRootAsync(rootId, TestContext.Current.CancellationToken);
        Assert.Equal(4096, Assert.Single(stored).SizeBytes);
    }

    [Fact]
    public async Task SynchronizeRootAsync_KeepsRootsSeparateEvenWhenFileNamesMatch()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var first = await fixture.AddRootAsync("First");
        var second = await fixture.AddRootAsync("Second");

        await fixture.Index.SynchronizeRootAsync(
            first, [Entry(first, 1, "shared-name.txt")], TestContext.Current.CancellationToken);
        await fixture.Index.SynchronizeRootAsync(
            second, [Entry(second, 1, "shared-name.txt")], TestContext.Current.CancellationToken);

        Assert.Single(await fixture.Index.ListForRootAsync(first, TestContext.Current.CancellationToken));
        Assert.Single(await fixture.Index.ListForRootAsync(second, TestContext.Current.CancellationToken));
        Assert.All(
            await fixture.Index.ListForRootAsync(first, TestContext.Current.CancellationToken),
            file => Assert.Equal(first, file.RootId));
    }

    [Fact]
    public async Task SynchronizeRootAsync_RefusesEntriesBelongingToAnotherRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var first = await fixture.AddRootAsync("First");
        var second = await fixture.AddRootAsync("Second");

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Index.SynchronizeRootAsync(
            first,
            [Entry(second, 1, "elsewhere.txt")],
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SynchronizeRootAsync_RefusesDuplicateFileIdentitiesInOnePass()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, "one.txt"), Entry(rootId, 1, "two.txt")],
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectingARootAlsoForgetsEverythingIndexedAboutIt()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId, [Entry(rootId, 1, "notes.txt")], TestContext.Current.CancellationToken);

        await fixture.Roots.RemoveAsync(rootId, TestContext.Current.CancellationToken);

        Assert.Empty(await fixture.Index.ListForRootAsync(rootId, TestContext.Current.CancellationToken));
        Assert.Equal(0, (await fixture.Index.GetStatisticsAsync(rootId, TestContext.Current.CancellationToken)).FileCount);
    }

    [Fact]
    public async Task ClearRootAsync_ForgetsOnlyTheNamedRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var first = await fixture.AddRootAsync("First");
        var second = await fixture.AddRootAsync("Second");
        await fixture.Index.SynchronizeRootAsync(
            first, [Entry(first, 1, "one.txt")], TestContext.Current.CancellationToken);
        await fixture.Index.SynchronizeRootAsync(
            second, [Entry(second, 1, "two.txt")], TestContext.Current.CancellationToken);

        await fixture.Index.ClearRootAsync(first, TestContext.Current.CancellationToken);

        Assert.Empty(await fixture.Index.ListForRootAsync(first, TestContext.Current.CancellationToken));
        Assert.Single(await fixture.Index.ListForRootAsync(second, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatisticsAsync_SummarizesOnlyTheNamedRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, "one.txt", sizeBytes: 100), Entry(rootId, 2, "two.txt", sizeBytes: 250)],
            TestContext.Current.CancellationToken);

        var statistics = await fixture.Index.GetStatisticsAsync(rootId, TestContext.Current.CancellationToken);

        Assert.Equal(2, statistics.FileCount);
        Assert.Equal(350, statistics.TotalSizeBytes);
        Assert.Equal(Moment, statistics.LastIndexedAtUtc);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReportsNothingForAnUnknownRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();

        var statistics = await fixture.Index.GetStatisticsAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(FileIndexStatistics.Empty, statistics);
    }

    [Fact]
    public async Task SynchronizeRootAsync_RefusesToStoreEntriesForARootThatWasNeverAuthorized()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var strangerId = Guid.NewGuid();

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Index.SynchronizeRootAsync(
            strangerId,
            [Entry(strangerId, 1, "notes.txt")],
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoredRowsNeverContainAnAbsolutePath()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId, [Entry(rootId, 1, @"Study\notes.txt")], TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path FROM indexed_files;";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var path = reader.GetString(0);
            Assert.False(System.IO.Path.IsPathRooted(path));
            Assert.DoesNotContain(':', path);
        }
    }

    [Fact]
    public async Task SearchRootAsync_MatchesTextAnywhereInThePath()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, @"Study\budget notes.txt"),
                Entry(rootId, 2, @"Budget\summary.txt"),
                Entry(rootId, 3, "holiday.txt"),
            ],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(pathContains: "budget"),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [@"Budget\summary.txt", @"Study\budget notes.txt"],
            results.Select(file => file.RelativePath));
    }

    /// <summary>
    /// The root is a separate argument precisely so a query cannot widen its own scope.
    /// This is the test that would fail if search ever leaked across authorized folders.
    /// </summary>
    [Fact]
    public async Task SearchRootAsync_NeverReturnsAnotherRootsFiles()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var mine = await fixture.AddRootAsync("Mine");
        var theirs = await fixture.AddRootAsync("Theirs");
        await fixture.Index.SynchronizeRootAsync(
            mine,
            [Entry(mine, 1, "budget.txt")],
            TestContext.Current.CancellationToken);
        await fixture.Index.SynchronizeRootAsync(
            theirs,
            [Entry(theirs, 2, "budget.txt")],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            mine,
            new SearchQuery(pathContains: "budget"),
            TestContext.Current.CancellationToken);

        Assert.All(results, file => Assert.Equal(mine, file.RootId));
        Assert.Single(results);
    }

    /// <summary>
    /// A file genuinely named with an underscore must not turn into a single-character
    /// wildcard, which would silently widen the search.
    /// </summary>
    [Fact]
    public async Task SearchRootAsync_TreatsLikeWildcardsInSearchTextAsLiteralCharacters()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, "report_final.txt"), Entry(rootId, 2, "reportXfinal.txt")],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(pathContains: "report_final"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["report_final.txt"], results.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SearchRootAsync_FiltersByFileEnding()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "notes.txt"),
                Entry(rootId, 2, "photo.PNG"),
                Entry(rootId, 3, "sheet.xlsx"),
            ],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(extensions: ["png", ".XLSX"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(["photo.PNG", "sheet.xlsx"], results.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SearchRootAsync_FiltersByCategoryAndKind()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "notes.txt"),
                Entry(rootId, 2, "photo.png", kind: FileKind.Image, category: FileCategory.Images),
                Entry(rootId, 3, "clip.mp4", kind: FileKind.Video, category: FileCategory.Videos),
            ],
            TestContext.Current.CancellationToken);

        var byCategory = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(categories: [FileCategory.Images, FileCategory.Videos]),
            TestContext.Current.CancellationToken);
        var byKind = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(kinds: [FileKind.Image]),
            TestContext.Current.CancellationToken);

        Assert.Equal(["clip.mp4", "photo.png"], byCategory.Select(file => file.RelativePath));
        Assert.Equal(["photo.png"], byKind.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SearchRootAsync_FiltersBySizeRangeInclusively()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "small.txt", sizeBytes: 100),
                Entry(rootId, 2, "medium.txt", sizeBytes: 500),
                Entry(rootId, 3, "large.txt", sizeBytes: 900),
            ],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(minSizeBytes: 100, maxSizeBytes: 500),
            TestContext.Current.CancellationToken);

        Assert.Equal(["medium.txt", "small.txt"], results.Select(file => file.RelativePath));
    }

    /// <summary>
    /// Stored timestamps keep the offset the file carried, so comparing the raw strings
    /// would rank "09:00+02:00" (07:00 UTC) after "08:00+00:00". The range must be judged
    /// in UTC, which is what this test pins down.
    /// </summary>
    [Fact]
    public async Task SearchRootAsync_ComparesDatesInUtcEvenWhenStoredOffsetsDiffer()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        var earlyInUtcButLaterOnTheClock = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(2));
        var lateInUtc = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "early.txt", modifiedAtUtc: earlyInUtcButLaterOnTheClock),
                Entry(rootId, 2, "late.txt", modifiedAtUtc: lateInUtc),
            ],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(modifiedAfterUtc: new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);

        Assert.Equal(["late.txt"], results.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SearchRootAsync_CapsResultsAtTheQueryLimit()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        var entries = Enumerable
            .Range(1, 10)
            .Select(index => Entry(rootId, index, $"file{index:D2}.txt"))
            .ToArray();
        await fixture.Index.SynchronizeRootAsync(rootId, entries, TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(limit: 3),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
        Assert.Equal(["file01.txt", "file02.txt", "file03.txt"], results.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SearchRootAsync_WithNoFiltersReturnsTheWholeRootUpToTheLimit()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, "a.txt"), Entry(rootId, 2, "b.txt")],
            TestContext.Current.CancellationToken);

        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt", "b.txt"], results.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task SearchRootAsync_ReturnsNothingForARootThatWasForgotten()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, "budget.txt")],
            TestContext.Current.CancellationToken);

        await fixture.Index.ClearRootAsync(rootId, TestContext.Current.CancellationToken);
        var results = await fixture.Index.SearchRootAsync(
            rootId,
            new SearchQuery(pathContains: "budget"),
            TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSizeCountsAsync_CountsFilesSharingAnExactSize()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "a.txt", sizeBytes: 50_000),
                Entry(rootId, 2, "b.txt", sizeBytes: 50_000),
                Entry(rootId, 3, "c.txt", sizeBytes: 90_000),
            ],
            TestContext.Current.CancellationToken);

        var groups = await fixture.Index.GetSizeCountsAsync(
            rootId, minimumSizeBytes: 4096, TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(groups, group => group.SizeBytes == 50_000).FileCount);
        Assert.Equal(1, Assert.Single(groups, group => group.SizeBytes == 90_000).FileCount);
    }

    /// <summary>
    /// Sizes seen once are still reported. A file copied into a second connected folder
    /// appears once in each, and filtering per root would hide exactly that case.
    /// </summary>
    [Fact]
    public async Task GetSizeCountsAsync_KeepsSizesSeenOnlyOnceSoCrossFolderCopiesSurvive()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [Entry(rootId, 1, "only.txt", sizeBytes: 60_000)],
            TestContext.Current.CancellationToken);

        var groups = await fixture.Index.GetSizeCountsAsync(
            rootId, minimumSizeBytes: 4096, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(groups).FileCount);
    }

    [Fact]
    public async Task GetSizeCountsAsync_IgnoresFilesBelowTheMinimumSize()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "tiny-a.txt", sizeBytes: 10),
                Entry(rootId, 2, "tiny-b.txt", sizeBytes: 10),
                Entry(rootId, 3, "big.txt", sizeBytes: 80_000),
            ],
            TestContext.Current.CancellationToken);

        var groups = await fixture.Index.GetSizeCountsAsync(
            rootId, minimumSizeBytes: 4096, TestContext.Current.CancellationToken);

        Assert.Equal(80_000, Assert.Single(groups).SizeBytes);
    }

    [Fact]
    public async Task GetSizeCountsAsync_ReportsOnlyTheNamedRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var mine = await fixture.AddRootAsync("Mine");
        var theirs = await fixture.AddRootAsync("Theirs");
        await fixture.Index.SynchronizeRootAsync(
            mine, [Entry(mine, 1, "mine.txt", sizeBytes: 70_000)], TestContext.Current.CancellationToken);
        await fixture.Index.SynchronizeRootAsync(
            theirs, [Entry(theirs, 2, "theirs.txt", sizeBytes: 70_000)], TestContext.Current.CancellationToken);

        var groups = await fixture.Index.GetSizeCountsAsync(
            mine, minimumSizeBytes: 4096, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(groups).FileCount);
    }

    [Fact]
    public async Task SummarizeRootAsync_GroupsSizeByCategoryLargestFirst()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "note.txt", sizeBytes: 100),
                Entry(rootId, 2, "photo.png", sizeBytes: 900, kind: FileKind.Image, category: FileCategory.Images),
                Entry(rootId, 3, "shot.png", sizeBytes: 300, kind: FileKind.Image, category: FileCategory.Images),
            ],
            TestContext.Current.CancellationToken);

        var summary = await fixture.Index.SummarizeRootAsync(
            rootId,
            Moment.AddYears(-1),
            largestFileCount: 5,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [FileCategory.Images, FileCategory.Documents],
            summary.Categories.Select(usage => usage.Category));
        var images = summary.Categories[0];
        Assert.Equal(2, images.FileCount);
        Assert.Equal(1200, images.TotalSizeBytes);
    }

    [Fact]
    public async Task SummarizeRootAsync_ReturnsTheLargestFilesInOrderAndHonoursTheLimit()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "small.txt", sizeBytes: 10),
                Entry(rootId, 2, "huge.txt", sizeBytes: 5000),
                Entry(rootId, 3, "medium.txt", sizeBytes: 500),
            ],
            TestContext.Current.CancellationToken);

        var summary = await fixture.Index.SummarizeRootAsync(
            rootId,
            Moment.AddYears(-1),
            largestFileCount: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(["huge.txt", "medium.txt"], summary.LargestFiles.Select(file => file.RelativePath));
    }

    /// <summary>
    /// The age cut-off is compared in UTC for the same reason search ranges are: stored
    /// timestamps keep whatever offset the file carried.
    /// </summary>
    [Fact]
    public async Task SummarizeRootAsync_CountsOnlyFilesUnchangedBeforeTheCutOff()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");
        await fixture.Index.SynchronizeRootAsync(
            rootId,
            [
                Entry(rootId, 1, "old.txt", sizeBytes: 700, modifiedAtUtc: Moment.AddYears(-2)),
                Entry(rootId, 2, "recent.txt", sizeBytes: 200, modifiedAtUtc: Moment),
            ],
            TestContext.Current.CancellationToken);

        var summary = await fixture.Index.SummarizeRootAsync(
            rootId,
            Moment.AddYears(-1),
            largestFileCount: 5,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.OldFileCount);
        Assert.Equal(700, summary.OldFileBytes);
    }

    [Fact]
    public async Task SummarizeRootAsync_ReportsNothingForAnEmptyRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var rootId = await fixture.AddRootAsync("Practice");

        var summary = await fixture.Index.SummarizeRootAsync(
            rootId,
            Moment.AddYears(-1),
            largestFileCount: 5,
            TestContext.Current.CancellationToken);

        Assert.Empty(summary.Categories);
        Assert.Empty(summary.LargestFiles);
        Assert.Equal(0, summary.OldFileCount);
    }

    [Fact]
    public async Task SummarizeRootAsync_SummarizesOnlyTheNamedRoot()
    {
        await using var fixture = await IndexFixture.CreateAsync();
        var mine = await fixture.AddRootAsync("Mine");
        var theirs = await fixture.AddRootAsync("Theirs");
        await fixture.Index.SynchronizeRootAsync(
            mine, [Entry(mine, 1, "mine.txt", sizeBytes: 100)], TestContext.Current.CancellationToken);
        await fixture.Index.SynchronizeRootAsync(
            theirs, [Entry(theirs, 2, "theirs.txt", sizeBytes: 900)], TestContext.Current.CancellationToken);

        var summary = await fixture.Index.SummarizeRootAsync(
            mine,
            Moment.AddYears(-1),
            largestFileCount: 5,
            TestContext.Current.CancellationToken);

        Assert.Equal("mine.txt", Assert.Single(summary.LargestFiles).RelativePath);
        Assert.Equal(100, Assert.Single(summary.Categories).TotalSizeBytes);
    }

    private static IndexedFile Entry(
        Guid rootId,
        int seed,
        string relativePath,
        long sizeBytes = 10,
        FileKind kind = FileKind.Document,
        FileCategory category = FileCategory.Documents,
        DateTimeOffset? modifiedAtUtc = null) => new(
        rootId,
        new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
        relativePath,
        kind,
        category,
        sizeBytes,
        Moment,
        modifiedAtUtc ?? Moment,
        Moment);

    private sealed class IndexFixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox;

        private IndexFixture(TemporaryDirectory sandbox, string databasePath)
        {
            _sandbox = sandbox;
            DatabasePath = databasePath;
            var options = Options.Create(new DatabaseOptions { DatabasePath = databasePath });
            Index = new SqliteFileIndex(options);
            Roots = new SqliteAuthorizedRootRepository(options, new SystemClock());
        }

        public string DatabasePath { get; }

        public SqliteFileIndex Index { get; }

        public SqliteAuthorizedRootRepository Roots { get; }

        public static async Task<IndexFixture> CreateAsync()
        {
            var sandbox = new TemporaryDirectory();
            var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
            var initializer = new SqliteDatabaseInitializer(
                Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
                new SystemClock(),
                NullLogger<SqliteDatabaseInitializer>.Instance);
            await initializer.InitializeAsync(TestContext.Current.CancellationToken);
            return new IndexFixture(sandbox, databasePath);
        }

        public async Task<Guid> AddRootAsync(string name)
        {
            var rootId = Guid.NewGuid();
            await Roots.SaveAsync(
                AuthorizedRoot.Create(
                    rootId,
                    System.IO.Path.Combine(_sandbox.Path, name),
                    name,
                    RootAccessLevel.Allowed,
                    RootAuthorizationScope.MetadataOnly),
                TestContext.Current.CancellationToken);
            return rootId;
        }

        public ValueTask DisposeAsync()
        {
            _sandbox.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
