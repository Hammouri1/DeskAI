using DeskAI.Core.Appearance;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteAppearanceSettingsRepositoryTests
{
    [Fact]
    public async Task LoadAsync_StartsWithTheDefaultLookFollowingWindows()
    {
        await using var world = await World.CreateAsync();

        var settings = await world.Repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppearanceSettings.Default, settings);
    }

    [Fact]
    public async Task Repository_RoundTripsTheChoice_AndOverwritesIt()
    {
        await using var world = await World.CreateAsync();

        await world.Repository.SaveAsync(new AppearanceSettings(ThemeMode.Dark, "ocean"), TestContext.Current.CancellationToken);
        Assert.Equal(new AppearanceSettings(ThemeMode.Dark, "ocean"), await world.Repository.LoadAsync(TestContext.Current.CancellationToken));

        await world.Repository.SaveAsync(new AppearanceSettings(ThemeMode.Light, "sand"), TestContext.Current.CancellationToken);
        Assert.Equal(new AppearanceSettings(ThemeMode.Light, "sand"), await world.Repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A look removed in a later version, or a corrupted row, must not stop the window being drawn.</summary>
    [Fact]
    public async Task LoadAsync_FallsBackToTheDefaultForValuesItDoesNotRecognise()
    {
        await using var world = await World.CreateAsync();
        await world.ExecuteAsync(
            "INSERT INTO app_settings(key, value, updated_at_utc) VALUES ('appearance.mode', 'Neon', '2026-09-16T00:00:00Z'), ('appearance.look', 'lava', '2026-09-16T00:00:00Z');");

        var settings = await world.Repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppearanceSettings.Default, settings);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly TemporaryDirectory _sandbox = new();

        private World() => DatabasePath = Path.Combine(_sandbox.Path, "deskai.db");

        public string DatabasePath { get; }

        public SqliteAppearanceSettingsRepository Repository { get; private set; } = null!;

        public static async Task<World> CreateAsync()
        {
            var world = new World();
            var options = Options.Create(new DatabaseOptions { DatabasePath = world.DatabasePath });
            await new SqliteDatabaseInitializer(
                options,
                new SystemClock(),
                NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync(TestContext.Current.CancellationToken);
            world.Repository = new SqliteAppearanceSettingsRepository(options, new SystemClock());
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
