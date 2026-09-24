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
            INSERT INTO authorized_roots(id, canonical_path, display_name, permission, created_at_utc, authorization_scope)
            VALUES ($id, $path, $name, $permission, $created, $scope)
            ON CONFLICT(id) DO UPDATE SET
                canonical_path = excluded.canonical_path,
                display_name = excluded.display_name,
                permission = excluded.permission,
                authorization_scope = excluded.authorization_scope;
            """;
        command.Parameters.AddWithValue("$id", root.Id.ToString("D"));
        command.Parameters.AddWithValue("$path", root.CanonicalPath);
        command.Parameters.AddWithValue("$name", root.DisplayName);
        command.Parameters.AddWithValue("$permission", (int)root.Permission);
        command.Parameters.AddWithValue("$created", clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$scope", (int)root.AuthorizationScope);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthorizedRoot?> FindAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.canonical_path, r.display_name, r.permission, r.authorization_scope, t.granted_at_utc, f.granted_at_utc
            FROM authorized_roots r LEFT JOIN tidy_permissions t ON t.root_id = r.id
            LEFT JOIN folder_move_permissions f ON f.root_id = r.id
            WHERE r.id = $id;
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? AuthorizedRoot.Create(
                    rootId, reader.GetString(0), reader.GetString(1),
                    (RootAccessLevel)reader.GetInt32(2), (RootAuthorizationScope)reader.GetInt32(3))
                .WithTidyAllowedSince(ReadGrant(reader, 4))
                .WithFolderMovesAllowedSince(ReadGrant(reader, 5))
            : null;
    }

    public async Task<IReadOnlyList<AuthorizedRoot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.canonical_path, r.display_name, r.permission, r.authorization_scope, t.granted_at_utc, f.granted_at_utc
            FROM authorized_roots r LEFT JOIN tidy_permissions t ON t.root_id = r.id
            LEFT JOIN folder_move_permissions f ON f.root_id = r.id
            ORDER BY r.display_name COLLATE NOCASE;
            """;
        var roots = new List<AuthorizedRoot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            roots.Add(AuthorizedRoot.Create(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                    (RootAccessLevel)reader.GetInt32(3), (RootAuthorizationScope)reader.GetInt32(4))
                .WithTidyAllowedSince(ReadGrant(reader, 5))
                .WithFolderMovesAllowedSince(ReadGrant(reader, 6)));
        }

        return roots;
    }

    public async Task RemoveAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // Only folders connected for reading may be removed here. The practice workspace and
        // any folder connected for organizing are excluded on purpose, so a revoke path
        // meant for the folder list cannot reach a scope that can change files. Both reading
        // scopes are listed, or a folder whose contents someone allowed could never be
        // disconnected again.
        //
        // Disconnecting forgets everything remembered about the folder, and that includes what
        // tidying it did: the plans and the journal hold the names of files in it. They are
        // erased first, in the same transaction, because they refer to the folder and would
        // otherwise block its removal. Every statement is limited to a folder that passes the
        // scope condition, so the practice workspace's history is never touched. Undo links go
        // before the records they join, since a link to an original record forbids removing it.
        const string removable = """
            SELECT id FROM authorized_roots
            WHERE id = $id AND authorization_scope IN ($metadataScope, $contentScope, $documentScope, $pdfScope, $slideScope, $pdfSlideScope)
            """;
        const string records = $"""
            SELECT t.id FROM execution_transactions t
            JOIN organization_plans p ON p.id = t.plan_id AND p.revision = t.plan_revision
            WHERE p.root_id IN ({removable})
            """;
        command.CommandText = $"""
            DELETE FROM undo_transaction_links
                WHERE undo_transaction_id IN ({records}) OR original_transaction_id IN ({records});
            DELETE FROM execution_transactions WHERE id IN ({records});
            DELETE FROM organization_plans WHERE root_id IN ({removable});
            DELETE FROM authorized_roots WHERE id IN ({removable});
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$metadataScope", (int)RootAuthorizationScope.MetadataOnly);
        command.Parameters.AddWithValue("$contentScope", (int)RootAuthorizationScope.MetadataAndContent);
        command.Parameters.AddWithValue("$documentScope", (int)RootAuthorizationScope.MetadataAndDocuments);
        command.Parameters.AddWithValue("$pdfScope", (int)RootAuthorizationScope.MetadataDocumentsAndPdf);
        command.Parameters.AddWithValue("$slideScope", (int)RootAuthorizationScope.MetadataDocumentsAndSlides);
        command.Parameters.AddWithValue("$pdfSlideScope", (int)RootAuthorizationScope.MetadataDocumentsPdfAndSlides);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AllowTidyAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Only a folder connected for reading can be tidied. Selecting the row through that
        // condition means a grant for any other folder simply inserts nothing.
        command.CommandText = """
            INSERT OR IGNORE INTO tidy_permissions(root_id, granted_at_utc)
            SELECT id, $granted FROM authorized_roots
            WHERE id = $id AND authorization_scope IN ($metadataScope, $contentScope, $documentScope, $pdfScope, $slideScope, $pdfSlideScope);
            """;
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        command.Parameters.AddWithValue("$granted", grantedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$metadataScope", (int)RootAuthorizationScope.MetadataOnly);
        command.Parameters.AddWithValue("$contentScope", (int)RootAuthorizationScope.MetadataAndContent);
        command.Parameters.AddWithValue("$documentScope", (int)RootAuthorizationScope.MetadataAndDocuments);
        command.Parameters.AddWithValue("$pdfScope", (int)RootAuthorizationScope.MetadataDocumentsAndPdf);
        command.Parameters.AddWithValue("$slideScope", (int)RootAuthorizationScope.MetadataDocumentsAndSlides);
        command.Parameters.AddWithValue("$pdfSlideScope", (int)RootAuthorizationScope.MetadataDocumentsPdfAndSlides);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopTidyAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tidy_permissions WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset? ReadGrant(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(ordinal),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
}
