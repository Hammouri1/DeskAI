using DeskAI.Core.Search;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteSavedSearchRepositoryTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveAsync_StoresAndReadsBackThePhraseUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var saved = SavedSearch.Create(Guid.NewGuid(), "Recent photos", "photos from last month", Moment);

        await fixture.Repository.SaveAsync(saved, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(saved.Id, stored.Id);
        Assert.Equal("Recent photos", stored.Name);
        Assert.Equal("photos from last month", stored.Phrase);
        Assert.Equal(Moment, stored.CreatedAtUtc);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.SaveAsync(
            SavedSearch.Create(Guid.NewGuid(), "Older", "photos", Moment),
            TestContext.Current.CancellationToken);
        await fixture.Repository.SaveAsync(
            SavedSearch.Create(Guid.NewGuid(), "Newer", "videos", Moment.AddHours(1)),
            TestContext.Current.CancellationToken);

        var stored = await fixture.Repository.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Newer", "Older"], stored.Select(item => item.Name));
    }

    /// <summary>
    /// Two saved searches must not be distinguishable only by capitalisation, which is why
    /// the unique index collates case-insensitively.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RefusesADuplicateNameRegardlessOfCase()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.SaveAsync(
            SavedSearch.Create(Guid.NewGuid(), "Photos", "photos", Moment),
            TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.SaveAsync(
                SavedSearch.Create(Guid.NewGuid(), "PHOTOS", "videos", Moment),
                TestContext.Current.CancellationToken));

        Assert.Contains("already exists", failure.Message, StringComparison.Ordinal);
        Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_UpdatesAnExistingSavedSearchInPlace()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await fixture.Repository.SaveAsync(
            SavedSearch.Create(id, "Photos", "photos", Moment),
            TestContext.Current.CancellationToken);

        await fixture.Repository.SaveAsync(
            SavedSearch.Create(id, "Photos", "photos over 10 mb", Moment),
            TestContext.Current.CancellationToken);

        var stored = Assert.Single(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("photos over 10 mb", stored.Phrase);
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyTheNamedSavedSearch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        await fixture.Repository.SaveAsync(
            SavedSearch.Create(keep, "Keep", "photos", Moment),
            TestContext.Current.CancellationToken);
        await fixture.Repository.SaveAsync(
            SavedSearch.Create(drop, "Drop", "videos", Moment),
            TestContext.Current.CancellationToken);

        await fixture.Repository.RemoveAsync(drop, TestContext.Current.CancellationToken);

        Assert.Equal(keep, Assert.Single(
            await fixture.Repository.ListAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task RemoveAsync_IsHarmlessWhenNothingMatches()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Repository.RemoveAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(await fixture.Repository.ListAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox;

        private Fixture(TemporaryDirectory sandbox, string databasePath)
        {
            _sandbox = sandbox;
            Repository = new SqliteSavedSearchRepository(
                Options.Create(new DatabaseOptions { DatabasePath = databasePath }));
        }

        public SqliteSavedSearchRepository Repository { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var sandbox = new TemporaryDirectory();
            var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
            var initializer = new SqliteDatabaseInitializer(
                Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
                new SystemClock(),
                NullLogger<SqliteDatabaseInitializer>.Instance);
            await initializer.InitializeAsync(TestContext.Current.CancellationToken);
            return new Fixture(sandbox, databasePath);
        }

        public ValueTask DisposeAsync()
        {
            _sandbox.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
