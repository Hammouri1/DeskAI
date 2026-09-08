using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
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

    private static IndexedFile Entry(Guid rootId, int seed, string relativePath, long sizeBytes = 10) => new(
        rootId,
        new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
        relativePath,
        FileKind.Document,
        FileCategory.Documents,
        sizeBytes,
        Moment,
        Moment,
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
