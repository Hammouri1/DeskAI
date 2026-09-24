using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>The separate yes to move things in a connected folder (ADR 0044), in its own table.</summary>
public sealed class SqliteFolderMovePermissions(IOptions<DatabaseOptions> options) : IFolderMovePermissions
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task AllowAsync(Guid rootId, DateTimeOffset grantedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Selected through the reading scopes, like the tidy grant: a yes for any other folder
        // simply inserts nothing.
        command.CommandText = """
            INSERT OR IGNORE INTO folder_move_permissions(root_id, granted_at_utc)
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

    public async Task StopAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM folder_move_permissions WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", rootId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
