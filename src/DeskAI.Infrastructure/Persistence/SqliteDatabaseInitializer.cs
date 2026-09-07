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
    public const int CurrentSchemaVersion = 6;
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
        await ApplyJournalMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyAuthorizationScopeMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyAiSettingsMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyAiUsageMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        LogDatabaseReady(logger, CurrentSchemaVersion);
    }

    private static async Task ApplyAiUsageMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ai_daily_usage (
                provider_id TEXT NOT NULL,
                utc_date TEXT NOT NULL,
                request_count INTEGER NOT NULL CHECK (request_count >= 0),
                PRIMARY KEY(provider_id, utc_date)
            );
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (6, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyAiSettingsMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ai_settings (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                mode INTEGER NOT NULL,
                provider_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                endpoint TEXT NULL,
                disclosures INTEGER NOT NULL,
                credential_reference TEXT NULL,
                timeout_seconds INTEGER NOT NULL,
                daily_request_limit INTEGER NOT NULL,
                max_estimated_cost_usd TEXT NULL,
                cloud_consent INTEGER NOT NULL
            );
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (5, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyAuthorizationScopeMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var hasColumn = false;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(authorized_roots);";
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), "authorization_scope", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!hasColumn)
        {
            await using var alter = connection.CreateCommand();
            alter.Transaction = (SqliteTransaction)transaction;
            alter.CommandText = "ALTER TABLE authorized_roots ADD COLUMN authorization_scope INTEGER NOT NULL DEFAULT 2;";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES ($version, $appliedAtUtc);";
        command.Parameters.AddWithValue("$version", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyJournalMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sql = """
            CREATE TABLE IF NOT EXISTS plan_issues (
                plan_id TEXT NOT NULL,
                plan_revision INTEGER NOT NULL,
                sequence INTEGER NOT NULL,
                code INTEGER NOT NULL,
                severity INTEGER NOT NULL,
                explanation TEXT NOT NULL,
                file_ids_json TEXT NOT NULL,
                operation_ids_json TEXT NOT NULL,
                PRIMARY KEY (plan_id, plan_revision, sequence),
                FOREIGN KEY (plan_id, plan_revision)
                    REFERENCES organization_plans(id, revision) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS execution_operation_journal (
                transaction_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                operation_id TEXT NOT NULL,
                kind INTEGER NOT NULL,
                source_relative_path TEXT NULL,
                destination_relative_path TEXT NOT NULL,
                before_size_bytes INTEGER NULL,
                before_modified_at_utc TEXT NULL,
                state INTEGER NOT NULL,
                error TEXT NULL,
                PRIMARY KEY (transaction_id, operation_id),
                FOREIGN KEY (transaction_id) REFERENCES execution_transactions(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS undo_transaction_links (
                undo_transaction_id TEXT NOT NULL PRIMARY KEY,
                original_transaction_id TEXT NOT NULL,
                FOREIGN KEY (undo_transaction_id) REFERENCES execution_transactions(id) ON DELETE CASCADE,
                FOREIGN KEY (original_transaction_id) REFERENCES execution_transactions(id) ON DELETE RESTRICT
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES ($version, $appliedAtUtc);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$version", 3);
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("$version", 2);
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
                created_at_utc TEXT NOT NULL,
                authorization_scope INTEGER NOT NULL DEFAULT 2
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES ($version, $appliedAtUtc);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$version", 1);
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
