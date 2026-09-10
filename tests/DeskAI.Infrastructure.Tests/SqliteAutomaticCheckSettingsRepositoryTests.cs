using DeskAI.Core.Rules;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteAutomaticCheckSettingsRepositoryTests
{
    /// <summary>
    /// A fresh install must be the quiet one: checks only while the app is open, and no
    /// notifications. If this ever inverts, people get behaviour they never chose.
    /// </summary>
    [Fact]
    public async Task LoadAsync_StartsWithTheQuietChoice()
    {
        await using var world = await World.CreateAsync();

        var settings = await world.Repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AutomaticCheckMode.WhileAppIsOpen, settings.Mode);
        Assert.False(settings.NotifyWhenSomethingIsFound);
        Assert.False(settings.IsPaused);
        Assert.Null(await world.Repository.ReadLastCheckedAtUtcAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repository_RoundTripsTheChoice()
    {
        await using var world = await World.CreateAsync();
        var expected = new AutomaticCheckSettings(
            AutomaticCheckMode.WhileAppIsOpen,
            AutomaticCheckFrequency.ACoupleOfTimesADay,
            IsPaused: true,
            NotifyWhenSomethingIsFound: true);

        await world.Repository.SaveAsync(expected, TestContext.Current.CancellationToken);

        Assert.Equal(expected, await world.Repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordCheckedAtAsync_RemembersWhenWithoutChangingTheChoice()
    {
        await using var world = await World.CreateAsync();
        var chosen = AutomaticCheckSettings.Default with
        {
            Frequency = AutomaticCheckFrequency.EveryHour,
            NotifyWhenSomethingIsFound = true,
        };
        await world.Repository.SaveAsync(chosen, TestContext.Current.CancellationToken);
        var moment = new DateTimeOffset(2026, 9, 10, 9, 30, 0, TimeSpan.Zero);

        await world.Repository.RecordCheckedAtAsync(moment, TestContext.Current.CancellationToken);

        Assert.Equal(moment, await world.Repository.ReadLastCheckedAtUtcAsync(TestContext.Current.CancellationToken));
        Assert.Equal(chosen, await world.Repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A row written by a newer version, or corrupted, must not become a mode no code
    /// handles — least of all on the setting that decides whether DeskAI runs unattended.
    /// Falling back to the narrow default is the only safe reading of a value we do not
    /// understand.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FallsBackToTheNarrowDefaultForAnUnknownStoredValue()
    {
        await using var world = await World.CreateAsync();
        await world.ExecuteAsync("""
            INSERT INTO automatic_check_settings(singleton_id, mode, frequency, is_paused, notify_on_findings)
            VALUES (1, 99, 99, 0, 0);
            """);

        var settings = await world.Repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AutomaticCheckMode.WhileAppIsOpen, settings.Mode);
        Assert.Equal(AutomaticCheckSettings.Default.Frequency, settings.Frequency);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox = new();

        private World() => DatabasePath = Path.Combine(_sandbox.Path, "deskai.db");

        public string DatabasePath { get; }

        public SqliteAutomaticCheckSettingsRepository Repository { get; private set; } = null!;

        public static async Task<World> CreateAsync()
        {
            var world = new World();
            var options = Options.Create(new DatabaseOptions { DatabasePath = world.DatabasePath });
            await new SqliteDatabaseInitializer(
                options,
                new SystemClock(),
                NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync(TestContext.Current.CancellationToken);
            world.Repository = new SqliteAutomaticCheckSettingsRepository(options);
            return world;
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
