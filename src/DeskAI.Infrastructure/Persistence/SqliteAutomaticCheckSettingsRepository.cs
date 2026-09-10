using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// Stores the automatic-check choice in the local database.
/// </summary>
/// <remarks>
/// Unrecognised stored values fall back to the default rather than being cast blindly into
/// the enum. A row written by a newer version, or corrupted, would otherwise become a mode
/// or frequency no code handles — and the failure would land on the setting that decides
/// whether DeskAI runs while nobody is looking.
/// </remarks>
public sealed class SqliteAutomaticCheckSettingsRepository(IOptions<DatabaseOptions> options)
    : IAutomaticCheckSettingsRepository
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<AutomaticCheckSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT mode, frequency, is_paused, notify_on_findings FROM automatic_check_settings WHERE singleton_id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return AutomaticCheckSettings.Default;
        }

        return new AutomaticCheckSettings(
            KnownOrDefault(reader.GetInt32(0), AutomaticCheckSettings.Default.Mode),
            KnownOrDefault(reader.GetInt32(1), AutomaticCheckSettings.Default.Frequency),
            reader.GetBoolean(2),
            reader.GetBoolean(3));
    }

    public async Task SaveAsync(AutomaticCheckSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automatic_check_settings(singleton_id, mode, frequency, is_paused, notify_on_findings)
            VALUES (1, $mode, $frequency, $paused, $notify)
            ON CONFLICT(singleton_id) DO UPDATE SET mode = excluded.mode, frequency = excluded.frequency,
                is_paused = excluded.is_paused, notify_on_findings = excluded.notify_on_findings;
            """;
        command.Parameters.AddWithValue("$mode", (int)settings.Mode);
        command.Parameters.AddWithValue("$frequency", (int)settings.Frequency);
        command.Parameters.AddWithValue("$paused", settings.IsPaused);
        command.Parameters.AddWithValue("$notify", settings.NotifyWhenSomethingIsFound);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> ReadLastCheckedAtUtcAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_checked_at_utc FROM automatic_check_settings WHERE singleton_id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text && DateTimeOffset.TryParse(
            text,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    public async Task RecordCheckedAtAsync(
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automatic_check_settings(singleton_id, mode, frequency, is_paused, notify_on_findings, last_checked_at_utc)
            VALUES (1, $mode, $frequency, 0, 0, $checkedAt)
            ON CONFLICT(singleton_id) DO UPDATE SET last_checked_at_utc = excluded.last_checked_at_utc;
            """;
        command.Parameters.AddWithValue("$mode", (int)AutomaticCheckSettings.Default.Mode);
        command.Parameters.AddWithValue("$frequency", (int)AutomaticCheckSettings.Default.Frequency);
        command.Parameters.AddWithValue("$checkedAt", checkedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TEnum KnownOrDefault<TEnum>(int stored, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.IsDefined(typeof(TEnum), stored) ? (TEnum)Enum.ToObject(typeof(TEnum), stored) : fallback;
}
