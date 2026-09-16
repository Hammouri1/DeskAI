using DeskAI.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>The <c>app_settings</c> key/value table, one row per key.</summary>
public sealed class SqliteAppSettingsStore(IOptions<DatabaseOptions> options, IClock clock) : IAppSettingsStore
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(key, value, updated_at_utc) VALUES ($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$now", clock.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
