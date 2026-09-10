using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteAutomaticCheckHistoryRepositoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Repository_RoundTripsARun()
    {
        await using var world = await World.CreateAsync();
        var run = Run(Start, AutomaticCheckOutcome.Completed, proposals: 3, catchUp: true);

        await world.Repository.AppendAsync(run, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await world.Repository.ListRecentAsync(10, TestContext.Current.CancellationToken));
        Assert.Equal(run, stored);
    }

    [Fact]
    public async Task ListRecentAsync_PutsTheNewestFirst()
    {
        await using var world = await World.CreateAsync();
        await world.Repository.AppendAsync(Run(Start), TestContext.Current.CancellationToken);
        await world.Repository.AppendAsync(Run(Start.AddHours(1)), TestContext.Current.CancellationToken);

        var runs = await world.Repository.ListRecentAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(Start.AddHours(1), runs[0].StartedAtUtc);
    }

    /// <summary>
    /// Checks happen as often as every fifteen minutes and record nothing anyone needs
    /// months later. Pruning happens in the same transaction as the insert, so there is no
    /// moment in which the bound is untrue.
    /// </summary>
    [Fact]
    public async Task AppendAsync_KeepsOnlyTheRecentWindow()
    {
        await using var world = await World.CreateAsync();
        var total = IAutomaticCheckHistoryRepository.MaximumRunsKept + 10;
        for (var index = 0; index < total; index++)
        {
            await world.Repository.AppendAsync(
                Run(Start.AddMinutes(index)),
                TestContext.Current.CancellationToken);
        }

        var kept = await world.CountAsync();
        var newest = await world.Repository.ListRecentAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(IAutomaticCheckHistoryRepository.MaximumRunsKept, kept);
        Assert.Equal(Start.AddMinutes(total - 1), newest[0].StartedAtUtc);
    }

    [Fact]
    public async Task ClearAsync_ForgetsEveryRecordedCheck()
    {
        await using var world = await World.CreateAsync();
        await world.Repository.AppendAsync(Run(Start), TestContext.Current.CancellationToken);

        await world.Repository.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await world.Repository.ListRecentAsync(10, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A row we cannot read must never be shown as though the check had gone fine.
    /// </summary>
    [Fact]
    public async Task ListRecentAsync_ReadsAnUnknownOutcomeAsAFailure()
    {
        await using var world = await World.CreateAsync();
        await world.ExecuteAsync("""
            INSERT INTO automatic_check_runs(
                run_id, started_at_utc, finished_at_utc, outcome,
                folders_checked, proposal_count, conflict_count, was_catch_up)
            VALUES ('11111111-1111-1111-1111-111111111111', '2026-09-10T08:00:00.0000000+00:00',
                    '2026-09-10T08:00:01.0000000+00:00', 99, 1, 0, 0, 0);
            """);

        var stored = Assert.Single(await world.Repository.ListRecentAsync(10, TestContext.Current.CancellationToken));

        Assert.Equal(AutomaticCheckOutcome.Failed, stored.Outcome);
    }

    private static AutomaticCheckRun Run(
        DateTimeOffset startedAt,
        AutomaticCheckOutcome outcome = AutomaticCheckOutcome.Completed,
        int proposals = 0,
        bool catchUp = false) =>
        new(Guid.NewGuid(), startedAt, startedAt.AddSeconds(2), outcome, 1, proposals, 0, catchUp);

    private sealed class World : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox = new();

        private World() => DatabasePath = Path.Combine(_sandbox.Path, "deskai.db");

        public string DatabasePath { get; }

        public SqliteAutomaticCheckHistoryRepository Repository { get; private set; } = null!;

        public static async Task<World> CreateAsync()
        {
            var world = new World();
            var options = Options.Create(new DatabaseOptions { DatabasePath = world.DatabasePath });
            await new SqliteDatabaseInitializer(
                options,
                new SystemClock(),
                NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync(TestContext.Current.CancellationToken);
            world.Repository = new SqliteAutomaticCheckHistoryRepository(options);
            return world;
        }

        public async Task<int> CountAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM automatic_check_runs;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            _sandbox.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
