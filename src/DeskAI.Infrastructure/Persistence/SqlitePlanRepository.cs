using System.Text.Json;
using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Plans;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

public sealed class SqlitePlanRepository(IOptions<DatabaseOptions> options) : IPlanRepository
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task SaveAsync(OrganizationPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var planCommand = connection.CreateCommand())
        {
            planCommand.Transaction = (SqliteTransaction)transaction;
            planCommand.CommandText = """
                INSERT INTO organization_plans(id, revision, root_id, created_at_utc, policy_version, state)
                VALUES ($id, $revision, $root, $created, $policy, $state)
                """;
            planCommand.Parameters.AddWithValue("$id", plan.Id.ToString("D"));
            planCommand.Parameters.AddWithValue("$revision", plan.Revision);
            planCommand.Parameters.AddWithValue("$root", plan.RootId.ToString("D"));
            planCommand.Parameters.AddWithValue("$created", plan.CreatedAtUtc.ToString("O"));
            planCommand.Parameters.AddWithValue("$policy", plan.PolicyVersion);
            planCommand.Parameters.AddWithValue("$state", (int)plan.State);
            await planCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < plan.Operations.Count; index++)
        {
            var operation = plan.Operations[index];
            var (source, destination) = Paths(operation);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO plan_operations(
                    operation_id, plan_id, plan_revision, sequence, kind,
                    source_relative_path, destination_relative_path, reason, provenance)
                VALUES ($operation, $plan, $revision, $sequence, $kind, $source, $destination, $reason, $provenance);
                """;
            command.Parameters.AddWithValue("$operation", operation.Id.ToString("D"));
            command.Parameters.AddWithValue("$plan", plan.Id.ToString("D"));
            command.Parameters.AddWithValue("$revision", plan.Revision);
            command.Parameters.AddWithValue("$sequence", index);
            command.Parameters.AddWithValue("$kind", (int)operation.Kind);
            command.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
            command.Parameters.AddWithValue("$destination", destination);
            command.Parameters.AddWithValue("$reason", operation.Reason);
            command.Parameters.AddWithValue("$provenance", (int)operation.Provenance);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < plan.Issues.Count; index++)
        {
            var issue = plan.Issues[index];
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO plan_issues(
                    plan_id, plan_revision, sequence, code, severity, explanation,
                    file_ids_json, operation_ids_json)
                VALUES ($plan, $revision, $sequence, $code, $severity, $explanation, $files, $operations);
                """;
            command.Parameters.AddWithValue("$plan", plan.Id.ToString("D"));
            command.Parameters.AddWithValue("$revision", plan.Revision);
            command.Parameters.AddWithValue("$sequence", index);
            command.Parameters.AddWithValue("$code", (int)issue.Code);
            command.Parameters.AddWithValue("$severity", (int)issue.Severity);
            command.Parameters.AddWithValue("$explanation", issue.Explanation);
            command.Parameters.AddWithValue("$files", JsonSerializer.Serialize(issue.FileIds));
            command.Parameters.AddWithValue("$operations", JsonSerializer.Serialize(issue.OperationIds));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrganizationPlan?> FindAsync(Guid planId, int revision, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        Guid rootId;
        DateTimeOffset created;
        string policy;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT root_id, created_at_utc, policy_version FROM organization_plans WHERE id = $id AND revision = $revision;";
            command.Parameters.AddWithValue("$id", planId.ToString("D"));
            command.Parameters.AddWithValue("$revision", revision);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            rootId = Guid.Parse(reader.GetString(0));
            created = DateTimeOffset.Parse(
                reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            policy = reader.GetString(2);
        }

        var operations = await ReadOperationsAsync(connection, planId, revision, cancellationToken).ConfigureAwait(false);
        var issues = await ReadIssuesAsync(connection, planId, revision, cancellationToken).ConfigureAwait(false);
        return OrganizationPlan.CreateDraft(planId, rootId, revision, created, policy, operations, issues);
    }

    private static async Task<List<PlanOperation>> ReadOperationsAsync(
        SqliteConnection connection, Guid planId, int revision, CancellationToken cancellationToken)
    {
        var result = new List<PlanOperation>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, kind, source_relative_path, destination_relative_path, reason, provenance
            FROM plan_operations WHERE plan_id = $plan AND plan_revision = $revision ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$plan", planId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = Guid.Parse(reader.GetString(0));
            var kind = (PlanOperationKind)reader.GetInt32(1);
            var source = reader.IsDBNull(2) ? null : reader.GetString(2);
            var destination = reader.GetString(3);
            var reason = reader.GetString(4);
            var provenance = (OperationProvenance)reader.GetInt32(5);
            result.Add(kind switch
            {
                PlanOperationKind.CreateDirectory => new CreateDirectoryOperation(id, destination, reason, provenance),
                PlanOperationKind.MoveFile => new MoveFileOperation(id, source!, destination, reason, provenance),
                PlanOperationKind.RenameFile => new RenameFileOperation(id, source!, destination, reason, provenance),
                _ => throw new InvalidDataException("Stored plan contains an unknown operation kind."),
            });
        }

        return result;
    }

    private static async Task<List<PlanIssue>> ReadIssuesAsync(
        SqliteConnection connection, Guid planId, int revision, CancellationToken cancellationToken)
    {
        var result = new List<PlanIssue>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code, severity, explanation, file_ids_json, operation_ids_json
            FROM plan_issues WHERE plan_id = $plan AND plan_revision = $revision ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$plan", planId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new PlanIssue(
                (PlanIssueCode)reader.GetInt32(0),
                (PlanIssueSeverity)reader.GetInt32(1),
                reader.GetString(2),
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(3)) ?? [],
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(4)) ?? []));
        }

        return result;
    }

    private static (string? Source, string Destination) Paths(PlanOperation operation) => operation switch
    {
        CreateDirectoryOperation create => (null, create.DestinationRelativePath),
        MoveFileOperation move => (move.SourceRelativePath, move.DestinationRelativePath),
        RenameFileOperation rename => (rename.SourceRelativePath, rename.DestinationRelativePath),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
