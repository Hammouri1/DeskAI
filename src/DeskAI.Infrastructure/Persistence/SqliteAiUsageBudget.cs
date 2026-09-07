using DeskAI.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

public sealed class SqliteAiUsageBudget(IOptions<DatabaseOptions> options) : IAiUsageBudget
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<bool> TryReserveRequestAsync(
        string providerId,
        int dailyLimit,
        DateOnly utcDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(dailyLimit, 1);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO ai_daily_usage(provider_id, utc_date, request_count)
            VALUES ($provider, $date, 1)
            ON CONFLICT(provider_id, utc_date) DO UPDATE SET request_count = request_count + 1
            WHERE request_count < $limit;
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$provider", providerId);
        command.Parameters.AddWithValue("$date", utcDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$limit", dailyLimit);
        var changed = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changed == 1;
    }

    public async Task<int> GetRequestCountAsync(
        string providerId,
        DateOnly utcDate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_count FROM ai_daily_usage WHERE provider_id = $provider AND utc_date = $date;";
        command.Parameters.AddWithValue("$provider", providerId);
        command.Parameters.AddWithValue("$date", utcDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
