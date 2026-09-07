using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

public sealed class SqliteOperationJournal(IOptions<DatabaseOptions> options) : IOperationJournal
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task CreateAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO execution_transactions(
                    id, plan_id, plan_revision, approval_id, state, started_at_utc, finished_at_utc, summary)
                VALUES ($id, $plan, $revision, $approval, $state, $started, $finished, NULL);
                """;
            command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
            command.Parameters.AddWithValue("$plan", entry.PlanId.ToString("D"));
            command.Parameters.AddWithValue("$revision", entry.PlanRevision);
            command.Parameters.AddWithValue("$approval", entry.ApprovalId.ToString("D"));
            command.Parameters.AddWithValue("$state", (int)entry.State);
            command.Parameters.AddWithValue("$started", entry.StartedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$finished", entry.FinishedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (entry.Kind == ExecutionTransactionKind.Undo && entry.OriginalTransactionId is Guid originalId)
        {
            await using var link = connection.CreateCommand();
            link.Transaction = (SqliteTransaction)transaction;
            link.CommandText = "INSERT INTO undo_transaction_links(undo_transaction_id, original_transaction_id) VALUES ($undo, $original);";
            link.Parameters.AddWithValue("$undo", entry.Id.ToString("D"));
            link.Parameters.AddWithValue("$original", originalId.ToString("D"));
            await link.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var operation in entry.Operations)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO execution_operation_journal(
                    transaction_id, sequence, operation_id, kind, source_relative_path,
                    destination_relative_path, before_size_bytes, before_modified_at_utc, state, error)
                VALUES ($transaction, $sequence, $operation, $kind, $source, $destination, $size, $modified, $state, $error);
                """;
            command.Parameters.AddWithValue("$transaction", entry.Id.ToString("D"));
            command.Parameters.AddWithValue("$sequence", operation.Sequence);
            command.Parameters.AddWithValue("$operation", operation.OperationId.ToString("D"));
            command.Parameters.AddWithValue("$kind", (int)operation.Kind);
            command.Parameters.AddWithValue("$source", (object?)operation.SourceRelativePath ?? DBNull.Value);
            command.Parameters.AddWithValue("$destination", operation.DestinationRelativePath);
            command.Parameters.AddWithValue("$size", operation.BeforeSizeBytes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$modified", operation.BeforeModifiedAtUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$state", (int)operation.State);
            command.Parameters.AddWithValue("$error", (object?)operation.Error ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateOperationAsync(
        Guid transactionId,
        Guid operationId,
        JournalOperationState state,
        string? failureMessage,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE execution_operation_journal SET state = $state, error = $error
            WHERE transaction_id = $transaction AND operation_id = $operation;
            """;
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$error", (object?)failureMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$transaction", transactionId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The journal operation could not be updated.");
        }
    }

    public async Task UpdateTransactionAsync(
        Guid transactionId,
        ExecutionTransactionState state,
        DateTimeOffset? finishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE execution_transactions SET state = $state, finished_at_utc = $finished WHERE id = $id;";
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$finished", finishedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", transactionId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The journal transaction could not be updated.");
        }
    }

    public async Task<ExecutionJournalEntry?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        return await FindAsync(connection, transactionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionJournalEntry>> ListRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCount, 100);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var ids = await ReadIdsAsync(
            connection,
            "SELECT id FROM execution_transactions ORDER BY started_at_utc DESC LIMIT $maximum;",
            maximumCount,
            cancellationToken).ConfigureAwait(false);
        return await ReadEntriesAsync(connection, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionJournalEntry>> ListIncompleteAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id FROM execution_transactions
            WHERE state IN ($prepared, $executing, $recovery) ORDER BY started_at_utc;
            """;
        command.Parameters.AddWithValue("$prepared", (int)ExecutionTransactionState.Prepared);
        command.Parameters.AddWithValue("$executing", (int)ExecutionTransactionState.Executing);
        command.Parameters.AddWithValue("$recovery", (int)ExecutionTransactionState.RecoveryRequired);
        var ids = new List<Guid>();
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        return await ReadEntriesAsync(connection, ids, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExecutionJournalEntry?> FindAsync(
        SqliteConnection connection,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        Guid planId;
        int revision;
        Guid approvalId;
        ExecutionTransactionState state;
        DateTimeOffset started;
        DateTimeOffset? finished;
        Guid? original;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT t.plan_id, t.plan_revision, t.approval_id, t.state,
                       t.started_at_utc, t.finished_at_utc, u.original_transaction_id
                FROM execution_transactions t
                LEFT JOIN undo_transaction_links u ON u.undo_transaction_id = t.id
                WHERE t.id = $id;
                """;
            command.Parameters.AddWithValue("$id", transactionId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            planId = Guid.Parse(reader.GetString(0));
            revision = reader.GetInt32(1);
            approvalId = Guid.Parse(reader.GetString(2));
            state = (ExecutionTransactionState)reader.GetInt32(3);
            started = ParseTimestamp(reader.GetString(4));
            finished = reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5));
            original = reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6));
        }

        var operations = new List<OperationJournalEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT sequence, operation_id, kind, source_relative_path, destination_relative_path,
                       before_size_bytes, before_modified_at_utc, state, error
                FROM execution_operation_journal WHERE transaction_id = $id ORDER BY sequence;
                """;
            command.Parameters.AddWithValue("$id", transactionId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                operations.Add(new OperationJournalEntry(
                    reader.GetInt32(0),
                    Guid.Parse(reader.GetString(1)),
                    (PlanOperationKind)reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
                    (JournalOperationState)reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        return new ExecutionJournalEntry(
            transactionId, planId, revision, approvalId,
            original is null ? ExecutionTransactionKind.Execute : ExecutionTransactionKind.Undo,
            original, state, started, finished, operations);
    }

    private static async Task<List<Guid>> ReadIdsAsync(
        SqliteConnection connection,
        string sql,
        int maximum,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$maximum", maximum);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(Guid.Parse(reader.GetString(0)));
        }

        return ids;
    }

    private static async Task<IReadOnlyList<ExecutionJournalEntry>> ReadEntriesAsync(
        SqliteConnection connection,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var result = new List<ExecutionJournalEntry>();
        foreach (var id in ids)
        {
            var entry = await FindAsync(connection, id, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
