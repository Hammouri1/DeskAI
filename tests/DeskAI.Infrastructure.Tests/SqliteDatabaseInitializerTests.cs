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

        Assert.Equal((long)SqliteDatabaseInitializer.CurrentSchemaVersion, version);

        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('app_settings', 'authorized_roots', 'organization_plans', 'plan_operations', 'execution_transactions', 'indexed_files');";
        Assert.Equal(6L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        // Every migration must record its own number so the upgrade path stays auditable.
        command.CommandText = "SELECT group_concat(version, ',') FROM (SELECT version FROM schema_migrations ORDER BY version);";
        Assert.Equal("1,2,3,4,5,6,7,8,9,10,11,12", await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
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
        Assert.Equal((long)SqliteDatabaseInitializer.CurrentSchemaVersion, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task InitializeAsync_AddsTheFileIndexToAnExistingDatabaseWithoutLosingData()
    {
        using var sandbox = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(sandbox.Path, "deskai.db");
        var initializer = new SqliteDatabaseInitializer(
            Options.Create(new DatabaseOptions { DatabasePath = databasePath }),
            new SystemClock(),
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        // Roll the sandbox database back to the shape a version-6 install would have.
        await using (var older = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await older.OpenAsync(TestContext.Current.CancellationToken);
            await using var downgrade = older.CreateCommand();
            downgrade.CommandText = """
                DROP TABLE indexed_files;
                DELETE FROM schema_migrations WHERE version = 7;
                INSERT INTO authorized_roots(id, canonical_path, display_name, permission, created_at_utc, authorization_scope)
                VALUES ('55555555-5555-5555-5555-555555555555', 'C:\\Sandbox\\Practice', 'Practice', 0, '2026-09-08T00:00:00Z', 0);
                """;
            await downgrade.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using var upgraded = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await upgraded.OpenAsync(TestContext.Current.CancellationToken);
        await using var verify = upgraded.CreateCommand();
        verify.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal((long)SqliteDatabaseInitializer.CurrentSchemaVersion, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        verify.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'indexed_files';";
        Assert.Equal(1L, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        verify.CommandText = "SELECT COUNT(*) FROM authorized_roots WHERE display_name = 'Practice';";
        Assert.Equal(1L, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_RemovingARootAlsoRemovesItsIndexedFiles()
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
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        command.CommandText = """
            INSERT INTO authorized_roots(id, canonical_path, display_name, permission, created_at_utc, authorization_scope)
            VALUES ('66666666-6666-6666-6666-666666666666', 'C:\\Sandbox\\Practice', 'Practice', 0, '2026-09-08T00:00:00Z', 0);
            INSERT INTO indexed_files(root_id, file_id, relative_path, name, extension, kind, category,
                                      size_bytes, created_at_utc, modified_at_utc, indexed_at_utc)
            VALUES ('66666666-6666-6666-6666-666666666666', '77777777-7777-7777-7777-777777777777',
                    'notes.txt', 'notes.txt', '.txt', 1, 1, 10,
                    '2026-09-08T00:00:00Z', '2026-09-08T00:00:00Z', '2026-09-08T00:00:00Z');
            DELETE FROM authorized_roots WHERE id = '66666666-6666-6666-6666-666666666666';
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        command.CommandText = "SELECT COUNT(*) FROM indexed_files;";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
