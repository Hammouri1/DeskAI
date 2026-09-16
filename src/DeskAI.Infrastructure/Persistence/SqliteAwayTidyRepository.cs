using System.Globalization;
using System.Text.Json;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Core.Tidy;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// The standing yes for tidying while away, and the unattended runs, per folder.
/// </summary>
/// <remarks>
/// Both tables reference <c>authorized_roots</c> with <c>ON DELETE CASCADE</c>, so disconnecting a
/// folder — or Start fresh — takes the approval and its runs with it. The approved rules are a
/// small JSON list of rule ID and version, decoded strictly: a row that cannot be read is
/// treated as no approval, never as one.
/// </remarks>
public sealed class SqliteAwayTidyRepository(IOptions<DatabaseOptions> options) : IAwayTidyRepository
{
    private sealed record StoredRule(Guid RuleId, int Version);

    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 4 };
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<AwayTidyApproval?> FindAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT root_id, approved_at_utc, rules_json, stopped_at_utc, stopped_reason
            FROM away_tidy WHERE root_id = $id;
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<AwayTidyApproval>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT root_id, approved_at_utc, rules_json, stopped_at_utc, stopped_reason
            FROM away_tidy ORDER BY approved_at_utc;
            """;
        var approvals = new List<AwayTidyApproval>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Read(reader) is { } approval)
            {
                approvals.Add(approval);
            }
        }

        return approvals.AsReadOnly();
    }

    public async Task SaveAsync(AwayTidyApproval approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Selecting the folder row through the tidy permission means a yes for a folder that may
        // not be tidied inserts nothing, whatever asked for it.
        command.CommandText = """
            INSERT INTO away_tidy(root_id, approved_at_utc, rules_json, stopped_at_utc, stopped_reason)
            SELECT root_id, $approved, $rules, $stoppedAt, $stoppedReason FROM tidy_permissions WHERE root_id = $id
            ON CONFLICT(root_id) DO UPDATE SET
                approved_at_utc = excluded.approved_at_utc,
                rules_json = excluded.rules_json,
                stopped_at_utc = excluded.stopped_at_utc,
                stopped_reason = excluded.stopped_reason;
            """;
        command.Parameters.AddWithValue("$id", approval.RootId.ToString("D"));
        command.Parameters.AddWithValue("$approved", approval.ApprovedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rules", JsonSerializer.Serialize(
            approval.Rules.Select(rule => new StoredRule(rule.RuleId, rule.Version)).ToArray(), Json));
        command.Parameters.AddWithValue("$stoppedAt", (object?)approval.StoppedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$stoppedReason", (object?)approval.StoppedReason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(Guid rootId, DateTimeOffset stoppedAtUtc, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE away_tidy SET stopped_at_utc = $at, stopped_reason = $reason WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$at", stoppedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM away_tidy WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendRunAsync(AwayTidyRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO away_tidy_runs(run_id, root_id, transaction_id, ran_at_utc, moved, folders_used, skipped, stopped_reason, seen_at_utc)
            VALUES ($id, $root, $transaction, $ranAt, $moved, $folders, $skipped, $reason, NULL);
            """;
        command.Parameters.AddWithValue("$id", run.Id.ToString("D"));
        command.Parameters.AddWithValue("$root", run.RootId.ToString("D"));
        command.Parameters.AddWithValue("$transaction", (object?)run.TransactionId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$ranAt", run.RanAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$moved", run.Moved);
        command.Parameters.AddWithValue("$folders", run.FoldersUsed);
        command.Parameters.AddWithValue("$skipped", run.Skipped);
        command.Parameters.AddWithValue("$reason", (object?)run.StoppedReason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AwayTidyRun>> ListUnseenRunsAsync(Guid rootId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, root_id, transaction_id, ran_at_utc, moved, folders_used, skipped, stopped_reason, seen_at_utc
            FROM away_tidy_runs WHERE root_id = $id AND seen_at_utc IS NULL
            ORDER BY ran_at_utc DESC, run_id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$limit", limit);
        var runs = new List<AwayTidyRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            runs.Add(new AwayTidyRun(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                ReadMoment(reader.GetString(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : ReadMoment(reader.GetString(8))));
        }

        return runs.AsReadOnly();
    }

    public async Task MarkRunsSeenAsync(Guid rootId, DateTimeOffset seenAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE away_tidy_runs SET seen_at_utc = $at WHERE root_id = $id AND seen_at_utc IS NULL;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$at", seenAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AwayTidyApproval? Read(SqliteDataReader reader)
    {
        StoredRule[]? rules;
        try
        {
            rules = JsonSerializer.Deserialize<StoredRule[]>(reader.GetString(2), Json);
        }
        catch (JsonException)
        {
            // A row that cannot be read is no approval. Failing closed here means a damaged
            // row can never let a run happen.
            return null;
        }

        if (rules is null)
        {
            return null;
        }

        return new AwayTidyApproval(
            Guid.Parse(reader.GetString(0)),
            rules.Select(rule => new ApprovedRuleVersion(rule.RuleId, rule.Version)).ToArray(),
            ReadMoment(reader.GetString(1)),
            reader.IsDBNull(3) ? null : ReadMoment(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static DateTimeOffset ReadMoment(string stored) =>
        DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
