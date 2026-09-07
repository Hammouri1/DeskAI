using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

public sealed class SqliteAuthorizedRootRepository(IOptions<DatabaseOptions> options, IClock clock)
    : IAuthorizedRootRepository
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task SaveAsync(AuthorizedRoot root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO authorized_roots(id, canonical_path, display_name, permission, created_at_utc)
            VALUES ($id, $path, $name, $permission, $created)
            ON CONFLICT(id) DO UPDATE SET
                canonical_path = excluded.canonical_path,
                display_name = excluded.display_name,
                permission = excluded.permission;
            """;
        command.Parameters.AddWithValue("$id", root.Id.ToString("D"));
        command.Parameters.AddWithValue("$path", root.CanonicalPath);
        command.Parameters.AddWithValue("$name", root.DisplayName);
        command.Parameters.AddWithValue("$permission", (int)root.Permission);
        command.Parameters.AddWithValue("$created", clock.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT canonical_path, display_name, permission FROM authorized_roots WHERE id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? AuthorizedRoot.Create(rootId, reader.GetString(0), reader.GetString(1), (RootAccessLevel)reader.GetInt32(2))
            : null;
    }
}
