using System.Globalization;
using System.Text.Json;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Studio;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// The Find groups board, one row per connected folder (ADR 0042).
/// </summary>
/// <remarks>
/// The row references <c>authorized_roots</c> with <c>ON DELETE CASCADE</c>, so disconnecting a
/// folder — or Start fresh — takes the board with it. A row that cannot be read loads as no
/// board, so a damaged row never reaches the page as an error.
/// </remarks>
public sealed class SqliteDesktopGroupRepository(IOptions<DatabaseOptions> options) : IDesktopGroupRepository
{
    private sealed record StoredGroup(string Name, string[] Items);

    private sealed record StoredBoard(StoredGroup[] Groups, string[] NotSure);

    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 4, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<DesktopGroupBoard?> LoadAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT source, made_at_utc, board_json FROM desktop_group_boards WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredBoard>(reader.GetString(2), Json);
            if (stored?.Groups is null || stored.NotSure is null || stored.Groups.Any(g => g?.Name is null || g.Items is null))
            {
                return null;
            }

            return new DesktopGroupBoard(
                rootId,
                stored.Groups.Select(g => new DesktopGroup(g.Name, g.Items)).ToList(),
                stored.NotSure,
                reader.GetInt32(0) == 1 ? DesktopGroupSource.LocalGuess : DesktopGroupSource.Ai,
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return null;
        }
    }

    public async Task SaveAsync(DesktopGroupBoard board, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        var json = JsonSerializer.Serialize(
            new StoredBoard(board.Groups.Select(g => new StoredGroup(g.Name, g.Items.ToArray())).ToArray(), board.NotSure.ToArray()),
            Json);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Selecting through authorized_roots means a board for a folder that is not connected
        // saves nothing, whatever asked for it.
        command.CommandText = """
            INSERT INTO desktop_group_boards(root_id, source, made_at_utc, board_json)
            SELECT id, $source, $madeAt, $json FROM authorized_roots WHERE id = $id
            ON CONFLICT(root_id) DO UPDATE SET
                source = excluded.source,
                made_at_utc = excluded.made_at_utc,
                board_json = excluded.board_json;
            """;
        command.Parameters.AddWithValue("$id", board.RootId.ToString("D"));
        command.Parameters.AddWithValue("$source", board.Source == DesktopGroupSource.LocalGuess ? 1 : 0);
        command.Parameters.AddWithValue("$madeAt", board.MadeAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$json", json);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
