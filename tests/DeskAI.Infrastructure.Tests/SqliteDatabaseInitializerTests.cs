using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Tests;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesVersionedFoundationSchemaInSandbox()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(sandbox.Path, "state", "deskai.db");
        var initializer = new SqliteDatabaseInitializer(
            Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
            new SystemClock(),
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var version = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6L, version);

        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('app_settings', 'authorized_roots', 'organization_plans', 'plan_operations', 'execution_transactions');";
        Assert.Equal(5L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_UpgradesVersionOneDatabaseToPlanningSchema()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_migrations (version INTEGER NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL);
                CREATE TABLE app_settings (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
                CREATE TABLE authorized_roots (id TEXT NOT NULL PRIMARY KEY, canonical_path TEXT NOT NULL, display_name TEXT NOT NULL, permission INTEGER NOT NULL, created_at_utc TEXT NOT NULL);
                INSERT INTO schema_migrations VALUES (1, '2026-09-07T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var initializer = new SqliteDatabaseInitializer(
            Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
            new SystemClock(),
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var upgraded = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await upgraded.OpenAsync(TestContext.Current.CancellationToken);
        await using var verify = upgraded.CreateCommand();
        verify.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal(6L, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        verify.CommandText = "SELECT COUNT(*) FROM pragma_table_info('authorized_roots') WHERE name = 'authorization_scope';";
        Assert.Equal(1L, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        verify.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'execution_transactions';";
        Assert.Equal(1L, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_PlanningSchemaEnforcesRootForeignKey()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
        var initializer = new SqliteDatabaseInitializer(
            Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
            new SystemClock(),
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO organization_plans(id, revision, root_id, created_at_utc, policy_version, state)
            VALUES ('plan', 1, 'missing-root', '2026-09-07T00:00:00Z', '1', 0);
            """;

        await Assert.ThrowsAsync<SqliteException>(() =>
            command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }
}
