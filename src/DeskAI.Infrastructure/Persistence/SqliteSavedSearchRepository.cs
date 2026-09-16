using System.Globalization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Search;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// Stores saved searches in the local SQLite database.
/// </summary>
/// <remarks>
/// Every statement is parameterized. Rows hold a name and a phrase only: there is no root
/// column, so this table can never describe what DeskAI is allowed to reach.
/// </remarks>
public sealed class SqliteSavedSearchRepository(IOptions<DatabaseOptions> options) : ISavedSearchRepository
{
    /// <summary>SQLite's extended result code for a unique-constraint violation.</summary>
    private const int UniqueConstraintViolation = 2067;

    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task SaveAsync(SavedSearch collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO saved_searches(saved_search_id, name, phrase, created_at_utc, is_pinned)
            VALUES ($id, $name, $phrase, $createdAtUtc, $isPinned)
            ON CONFLICT(saved_search_id) DO UPDATE SET
                name = excluded.name,
                phrase = excluded.phrase;
            """;
        command.Parameters.AddWithValue("$id", collection.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", collection.Name);
        command.Parameters.AddWithValue("$phrase", collection.Phrase);
        command.Parameters.AddWithValue("$createdAtUtc", collection.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$isPinned", collection.IsPinned ? 1 : 0);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == UniqueConstraintViolation)
        {
            // The case-insensitive unique index rejected a duplicate name. Surfaced as a
            // readable message so the UI can ask for a different one instead of failing.
            throw new InvalidOperationException(
                $"A saved search called \"{collection.Name}\" already exists.",
                exception);
        }
    }

    public async Task<IReadOnlyList<SavedSearch>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT saved_search_id, name, phrase, created_at_utc, is_pinned
            FROM saved_searches
            ORDER BY created_at_utc DESC;
            """;

        var collections = new List<SavedSearch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            collections.Add(SavedSearch.Create(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(
                    reader.GetString(3),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetInt32(4) != 0));
        }

        return collections.AsReadOnly();
    }

    public async Task SetPinnedAsync(Guid collectionId, bool isPinned, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE saved_searches SET is_pinned = $isPinned WHERE saved_search_id = $id;";
        command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0);
        command.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM saved_searches WHERE saved_search_id = $id;";
        command.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
