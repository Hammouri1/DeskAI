using DeskAI.Core.Abstractions;
using DeskAI.Core.Appearance;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// Stores the look and theme choice in the local database's key/value settings table.
/// </summary>
/// <remarks>
/// The <c>app_settings</c> table has existed since schema 1 for exactly this kind of small
/// setting, so no schema change is needed. A stored value nobody recognises — a look removed
/// later, a row written by a newer version — falls back to the default rather than being
/// trusted, so the window can always be drawn.
/// </remarks>
public sealed class SqliteAppearanceSettingsRepository(IOptions<DatabaseOptions> options, IClock clock)
    : IAppearanceSettingsRepository
{
    private const string ModeKey = "appearance.mode";
    private const string LookKey = "appearance.look";

    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<AppearanceSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM app_settings WHERE key IN ($mode, $look);";
        command.Parameters.AddWithValue("$mode", ModeKey);
        command.Parameters.AddWithValue("$look", LookKey);
        var mode = AppearanceSettings.Default.Mode;
        var look = AppearanceSettings.Default.LookId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var value = reader.GetString(1);
            switch (reader.GetString(0))
            {
                case ModeKey when Enum.TryParse<ThemeMode>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed):
                    mode = parsed;
                    break;
                case LookKey when DeskLookCatalog.Find(value) is not null:
                    look = value;
                    break;
            }
        }

        return new AppearanceSettings(mode, look);
    }

    public async Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(key, value, updated_at_utc) VALUES ($modeKey, $mode, $now), ($lookKey, $look, $now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$modeKey", ModeKey);
        command.Parameters.AddWithValue("$mode", settings.Mode.ToString());
        command.Parameters.AddWithValue("$lookKey", LookKey);
        command.Parameters.AddWithValue("$look", settings.LookId);
        command.Parameters.AddWithValue("$now", clock.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
