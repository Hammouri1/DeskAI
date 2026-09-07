using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

public sealed class SqliteAiSettingsRepository(IOptions<DatabaseOptions> options) : IAiSettingsRepository
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<AiSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT mode, provider_id, model_id, endpoint, disclosures, credential_reference, timeout_seconds, daily_request_limit, max_estimated_cost_usd, cloud_consent FROM ai_settings WHERE singleton_id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return AiSettings.Default;
        }

        var disclosures = Enum.GetValues<DisclosureCategory>()
            .Where(category => (reader.GetInt64(4) & (1L << (int)category)) != 0)
            .ToHashSet();
        return new AiSettings(
            (AiMode)reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            disclosures,
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.GetBoolean(9));
    }

    public async Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var disclosureFlags = settings.CloudDisclosures.Aggregate(0L, (value, category) => value | (1L << (int)category));
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ai_settings(singleton_id, mode, provider_id, model_id, endpoint, disclosures, credential_reference, timeout_seconds, daily_request_limit, max_estimated_cost_usd, cloud_consent)
            VALUES (1, $mode, $provider, $model, $endpoint, $disclosures, $credential, $timeout, $dailyLimit, $maxCost, $consent)
            ON CONFLICT(singleton_id) DO UPDATE SET mode = excluded.mode, provider_id = excluded.provider_id,
                model_id = excluded.model_id, endpoint = excluded.endpoint, disclosures = excluded.disclosures,
                credential_reference = excluded.credential_reference, timeout_seconds = excluded.timeout_seconds,
                daily_request_limit = excluded.daily_request_limit, max_estimated_cost_usd = excluded.max_estimated_cost_usd,
                cloud_consent = excluded.cloud_consent;
            """;
        command.Parameters.AddWithValue("$mode", (int)settings.Mode);
        command.Parameters.AddWithValue("$provider", settings.ProviderId);
        command.Parameters.AddWithValue("$model", settings.ModelId);
        command.Parameters.AddWithValue("$endpoint", (object?)settings.Endpoint ?? DBNull.Value);
        command.Parameters.AddWithValue("$disclosures", disclosureFlags);
        command.Parameters.AddWithValue("$credential", (object?)settings.CredentialReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$timeout", settings.TimeoutSeconds);
        command.Parameters.AddWithValue("$dailyLimit", settings.DailyRequestLimit);
        command.Parameters.AddWithValue("$maxCost", (object?)settings.MaximumEstimatedCostUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("$consent", settings.CloudConsentGranted);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
