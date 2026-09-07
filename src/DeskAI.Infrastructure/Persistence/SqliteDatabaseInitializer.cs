using Microsoft.Data.Sqlite;
using DeskAI.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

public sealed partial class SqliteDatabaseInitializer(
    IOptions<DatabaseOptions> options,
    IClock clock,
    ILogger<SqliteDatabaseInitializer> logger) : IDatabaseInitializer
{
    public const int CurrentSchemaVersion = 2;
    private readonly DatabaseOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();
        var parent = Path.GetDirectoryName(_options.DatabasePath)
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        Directory.CreateDirectory(parent);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        await ApplyInitialMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyPlanningMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        LogDatabaseReady(logger, CurrentSchemaVersion);
    }

    private static async Task ApplyPlanningMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sql = """
            CREATE TABLE IF NOT EXISTS organization_plans (
                id TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK (revision > 0),
                root_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                policy_version TEXT NOT NULL,
                state INTEGER NOT NULL,
                PRIMARY KEY (id, revision),
                FOREIGN KEY (root_id) REFERENCES authorized_roots(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS plan_operations (
                operation_id TEXT NOT NULL,
                plan_id TEXT NOT NULL,
                plan_revision INTEGER NOT NULL,
                sequence INTEGER NOT NULL CHECK (sequence >= 0),
                kind INTEGER NOT NULL,
                source_relative_path TEXT NULL,
                destination_relative_path TEXT NOT NULL,
                reason TEXT NOT NULL,
                provenance INTEGER NOT NULL,
                PRIMARY KEY (plan_id, plan_revision, operation_id),
                FOREIGN KEY (plan_id, plan_revision)
                    REFERENCES organization_plans(id, revision) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS execution_transactions (
                id TEXT NOT NULL PRIMARY KEY,
                plan_id TEXT NOT NULL,
                plan_revision INTEGER NOT NULL,
                approval_id TEXT NOT NULL,
                state INTEGER NOT NULL,
                started_at_utc TEXT NOT NULL,
                finished_at_utc TEXT NULL,
                summary TEXT NULL,
                FOREIGN KEY (plan_id, plan_revision)
                    REFERENCES organization_plans(id, revision) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_organization_plans_root_created
                ON organization_plans(root_id, created_at_utc);
            CREATE INDEX IF NOT EXISTS ix_execution_transactions_plan
                ON execution_transactions(plan_id, plan_revision, started_at_utc);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES ($version, $appliedAtUtc);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$version", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyInitialMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sql = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS authorized_roots (
                id TEXT NOT NULL PRIMARY KEY,
                canonical_path TEXT NOT NULL,
                display_name TEXT NOT NULL,
                permission INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES ($version, $appliedAtUtc);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$version", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "DeskAI local database is ready at schema version {SchemaVersion}.")]
    private static partial void LogDatabaseReady(ILogger logger, int schemaVersion);
}
