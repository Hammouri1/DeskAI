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
    public const int CurrentSchemaVersion = 12;
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
        await ApplyFileIndexMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplySavedSearchMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyAutomationRuleMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyAutomaticCheckMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyAutomaticCheckHistoryMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await ApplyTidyPermissionMigrationAsync(connection, clock.UtcNow, cancellationToken).ConfigureAwait(false);

        LogDatabaseReady(logger, CurrentSchemaVersion);
    }

    /// <summary>
    /// Adds the separate "you may tidy this folder" permission.
    /// </summary>
    /// <remarks>
    /// Its own table rather than a column or a new scope value, so that saving a folder's
    /// reading scope — which rewrites the folder row — can never drop or grant it, and so that
    /// disconnecting a folder erases it through the cascade like everything else remembered.
    /// </remarks>
    private static async Task ApplyTidyPermissionMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tidy_permissions (
                root_id        TEXT NOT NULL PRIMARY KEY REFERENCES authorized_roots(id) ON DELETE CASCADE,
                granted_at_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (12, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a short history of the checks that happened.
    /// </summary>
    /// <remarks>
    /// Kept apart from the operation journal on purpose. The journal exists to make file
    /// changes auditable and undoable; a check changes nothing, and recording checks in the
    /// journal would suggest otherwise. This table is also bounded — the repository discards
    /// all but a recent window — because a permanent record of when someone's folders were
    /// looked at is not a neutral thing to keep.
    /// </remarks>
    private static async Task ApplyAutomaticCheckHistoryMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS automatic_check_runs (
                run_id           TEXT NOT NULL PRIMARY KEY,
                started_at_utc   TEXT NOT NULL,
                finished_at_utc  TEXT NOT NULL,
                outcome          INTEGER NOT NULL,
                folders_checked  INTEGER NOT NULL,
                proposal_count   INTEGER NOT NULL,
                conflict_count   INTEGER NOT NULL,
                was_catch_up     INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_automatic_check_runs_started
                ON automatic_check_runs(started_at_utc DESC);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (11, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds how often DeskAI looks for rule matches, and when it last looked.
    /// </summary>
    /// <remarks>
    /// A single row, because there is one answer per installation. The frequency is stored
    /// as the choice a person made rather than as a number of minutes, so a later change to
    /// what "every hour" means cannot silently rewrite what they agreed to. Defaults are
    /// written here rather than assumed by the reader: the quiet settings — while the app is
    /// open, no notifications — must be what a fresh database actually contains.
    /// </remarks>
    private static async Task ApplyAutomaticCheckMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS automatic_check_settings (
                singleton_id       INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                mode               INTEGER NOT NULL,
                frequency          INTEGER NOT NULL,
                is_paused          INTEGER NOT NULL,
                notify_on_findings INTEGER NOT NULL,
                last_checked_at_utc TEXT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (10, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds automation rules.
    /// </summary>
    /// <remarks>
    /// Like saved searches, a rule stores no root reference. A rule must not outlive or
    /// widen an authorization, so which folders it could ever touch is resolved from the
    /// connected folders when it runs rather than captured when it is written. Conditions
    /// are stored as a JSON array of plain kind/value pairs and rebuilt through the closed
    /// set in <c>RuleCodec</c>, so a stored row can never name a type to construct. The
    /// unique index is case-insensitive so two rules cannot be told apart only by
    /// capitalisation.
    /// </remarks>
    private static async Task ApplyAutomationRuleMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS automation_rules (
                rule_id         TEXT NOT NULL PRIMARY KEY,
                name            TEXT NOT NULL,
                version         INTEGER NOT NULL,
                is_enabled      INTEGER NOT NULL,
                conditions_json TEXT NOT NULL,
                action_kind     TEXT NOT NULL,
                action_value    TEXT NOT NULL,
                created_at_utc  TEXT NOT NULL,
                updated_at_utc  TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_automation_rules_name
                ON automation_rules(name COLLATE NOCASE);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (9, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds saved searches.
    /// </summary>
    /// <remarks>
    /// The table stores a phrase and no root reference on purpose. A saved search must not
    /// be able to outlive or widen an authorization, so scope is resolved from the
    /// authorized roots each time one runs rather than captured here. The unique index is
    /// case-insensitive so two collections cannot be told apart only by capitalisation.
    /// </remarks>
    private static async Task ApplySavedSearchMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS saved_searches (
                saved_search_id   TEXT NOT NULL PRIMARY KEY,
                name            TEXT NOT NULL,
                phrase          TEXT NOT NULL,
                created_at_utc  TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_saved_searches_name
                ON saved_searches(name COLLATE NOCASE);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (8, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds the local metadata index. Rows cascade from their authorized root so that
    /// disconnecting a folder also erases everything DeskAI remembered about it.
    /// </summary>
    private static async Task ApplyFileIndexMigrationAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS indexed_files (
                root_id TEXT NOT NULL,
                file_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                name TEXT NOT NULL,
                extension TEXT NOT NULL,
                kind INTEGER NOT NULL,
                category INTEGER NOT NULL,
                size_bytes INTEGER NOT NULL CHECK (size_bytes >= 0),
                created_at_utc TEXT NOT NULL,
                modified_at_utc TEXT NOT NULL,
                indexed_at_utc TEXT NOT NULL,
                PRIMARY KEY (root_id, file_id),
                FOREIGN KEY (root_id) REFERENCES authorized_roots(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_indexed_files_root_path
                ON indexed_files(root_id, relative_path);
            CREATE INDEX IF NOT EXISTS ix_indexed_files_root_name
                ON indexed_files(root_id, name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_indexed_files_root_category
                ON indexed_files(root_id, category);
            CREATE INDEX IF NOT EXISTS ix_indexed_files_root_size
                ON indexed_files(root_id, size_bytes);
            CREATE INDEX IF NOT EXISTS ix_indexed_files_root_modified
                ON indexed_files(root_id, modified_at_utc);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (7, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", appliedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
        command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc) VALUES (4, $appliedAtUtc);";
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
