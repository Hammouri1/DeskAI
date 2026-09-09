using System.Text.Json;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// Stores rules in the local database.
/// </summary>
/// <remarks>
/// <para>
/// Conditions are held as a JSON array of plain kind/value pairs and rebuilt through
/// <see cref="RuleCodec"/>, which knows only a closed set of kinds. No type name is ever
/// read from the database, so a hand-edited or corrupt row cannot cause anything to be
/// constructed that a person could not have written in the app.
/// </para>
/// <para>
/// A row that cannot be understood throws rather than being skipped. Skipping would mean a
/// rule quietly stops running while still appearing to exist, and silently doing less than
/// someone asked for is its own kind of wrong answer.
/// </para>
/// </remarks>
public sealed class SqliteRuleRepository(IOptions<DatabaseOptions> options, IClock clock) : IRuleRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<IReadOnlyList<AutomationRule>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule_id, name, version, is_enabled, conditions_json, action_kind, action_value
            FROM automation_rules ORDER BY name COLLATE NOCASE;
            """;

        var rules = new List<AutomationRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rules.Add(Read(reader));
        }

        return rules.AsReadOnly();
    }

    public async Task<AutomationRule?> FindAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule_id, name, version, is_enabled, conditions_json, action_kind, action_value
            FROM automation_rules WHERE rule_id = $id;
            """;
        command.Parameters.AddWithValue("$id", ruleId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task SaveAsync(AutomationRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var conditions = rule.Conditions.Select(RuleCodec.Encode).ToArray();
        var action = RuleCodec.Encode(rule.Action);
        var now = clock.UtcNow.ToString("O");

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automation_rules(
                rule_id, name, version, is_enabled, conditions_json,
                action_kind, action_value, created_at_utc, updated_at_utc)
            VALUES ($id, $name, $version, $enabled, $conditions, $actionKind, $actionValue, $now, $now)
            ON CONFLICT(rule_id) DO UPDATE SET
                name = excluded.name,
                version = excluded.version,
                is_enabled = excluded.is_enabled,
                conditions_json = excluded.conditions_json,
                action_kind = excluded.action_kind,
                action_value = excluded.action_value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", rule.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$version", rule.Version);
        command.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$conditions", JsonSerializer.Serialize(conditions, Json));
        command.Parameters.AddWithValue("$actionKind", action.Kind);
        command.Parameters.AddWithValue("$actionValue", action.Value);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automation_rules WHERE rule_id = $id;";
        command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AutomationRule Read(SqliteDataReader reader)
    {
        var stored = JsonSerializer.Deserialize<RuleConditionData[]>(reader.GetString(4), Json)
            ?? throw new FormatException("A stored rule has no conditions.");

        return AutomationRule.Create(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            stored.Select(RuleCodec.DecodeCondition).ToArray(),
            RuleCodec.DecodeAction(new RuleActionData(reader.GetString(5), reader.GetString(6))),
            reader.GetInt32(2),
            reader.GetInt32(3) != 0);
    }
}
