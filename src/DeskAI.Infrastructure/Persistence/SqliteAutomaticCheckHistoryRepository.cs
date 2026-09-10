using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// Keeps the recent checks in the local database, and forgets the rest.
/// </summary>
/// <remarks>
/// Appending prunes in the same transaction, so the table cannot grow past
/// <see cref="IAutomaticCheckHistoryRepository.MaximumRunsKept"/> even if pruning were
/// forgotten elsewhere. Doing it on write rather than on a timer means there is no state in
/// which the bound is temporarily untrue.
/// </remarks>
public sealed class SqliteAutomaticCheckHistoryRepository(IOptions<DatabaseOptions> options)
    : IAutomaticCheckHistoryRepository
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task AppendAsync(AutomaticCheckRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO automatic_check_runs(
                run_id, started_at_utc, finished_at_utc, outcome,
                folders_checked, proposal_count, conflict_count, was_catch_up)
            VALUES ($id, $started, $finished, $outcome, $folders, $proposals, $conflicts, $catchUp);

            DELETE FROM automatic_check_runs
            WHERE run_id NOT IN (
                SELECT run_id FROM automatic_check_runs
                ORDER BY started_at_utc DESC, run_id DESC
                LIMIT $keep);
            """;
        command.Parameters.AddWithValue("$id", run.Id.ToString());
        command.Parameters.AddWithValue("$started", run.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$finished", run.FinishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", (int)run.Outcome);
        command.Parameters.AddWithValue("$folders", run.FoldersChecked);
        command.Parameters.AddWithValue("$proposals", run.ProposalCount);
        command.Parameters.AddWithValue("$conflicts", run.ConflictCount);
        command.Parameters.AddWithValue("$catchUp", run.WasCatchUp);
        command.Parameters.AddWithValue("$keep", IAutomaticCheckHistoryRepository.MaximumRunsKept);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AutomaticCheckRun>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, started_at_utc, finished_at_utc, outcome,
                   folders_checked, proposal_count, conflict_count, was_catch_up
            FROM automatic_check_runs
            ORDER BY started_at_utc DESC, run_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$limit",
            Math.Min(limit, IAutomaticCheckHistoryRepository.MaximumRunsKept));

        var runs = new List<AutomaticCheckRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            runs.Add(new AutomaticCheckRun(
                Guid.Parse(reader.GetString(0)),
                ReadMoment(reader.GetString(1)),
                ReadMoment(reader.GetString(2)),
                KnownOutcome(reader.GetInt32(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetBoolean(7)));
        }

        return runs.AsReadOnly();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automatic_check_runs;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset ReadMoment(string stored) => DateTimeOffset.Parse(
        stored,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);

    /// <summary>
    /// An unrecognised outcome is reported as a failure rather than as a completed run.
    /// A row we cannot read should never be shown as though the check had gone fine.
    /// </summary>
    private static AutomaticCheckOutcome KnownOutcome(int stored) =>
        Enum.IsDefined(typeof(AutomaticCheckOutcome), stored)
            ? (AutomaticCheckOutcome)stored
            : AutomaticCheckOutcome.Failed;
}
