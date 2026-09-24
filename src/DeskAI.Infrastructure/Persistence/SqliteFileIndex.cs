using System.Globalization;
using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Indexing;
using DeskAI.Core.Search;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Persistence;

/// <summary>
/// Stores remembered file metadata in the local SQLite database.
/// </summary>
/// <remarks>
/// Rows belong to an authorized root through a foreign key with ON DELETE CASCADE, so
/// disconnecting a folder also forgets everything the index remembered about it. Every
/// statement is parameterized and every read is filtered by root ID.
/// </remarks>
public sealed class SqliteFileIndex(IOptions<DatabaseOptions> options) : IFileIndex
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public async Task<FileIndexSyncResult> SynchronizeRootAsync(
        Guid rootId,
        IReadOnlyList<IndexedFile> files,
        FileIndexLook look,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(look);
        ArgumentOutOfRangeException.ThrowIfNegative(look.DeepFoldersSkipped);
        if (rootId == Guid.Empty)
        {
            throw new ArgumentException("Index changes must name an authorized root.", nameof(rootId));
        }

        if (files.Any(file => file.RootId != rootId))
        {
            throw new ArgumentException("Every entry must belong to the root being synchronized.", nameof(files));
        }

        var rootKey = rootId.ToString("D");
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sqliteTransaction = (SqliteTransaction)transaction;

        var existing = await ReadExistingAsync(connection, sqliteTransaction, rootId, cancellationToken)
            .ConfigureAwait(false);

        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var seen = new HashSet<Guid>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(file.FileId))
            {
                // A duplicate ID in one pass would silently drop a file; refuse instead.
                throw new ArgumentException("The same file ID was supplied twice in one refresh.", nameof(files));
            }

            if (existing.TryGetValue(file.FileId, out var stored) && stored.MatchesStoredFacts(file))
            {
                unchanged++;
                continue;
            }

            await WriteAsync(connection, sqliteTransaction, rootKey, file, cancellationToken).ConfigureAwait(false);
            if (existing.ContainsKey(file.FileId))
            {
                updated++;
            }
            else
            {
                added++;
            }
        }

        // A look that stopped early did not reach every file, so an unseen row is not evidence
        // that its file is gone. Forgetting it would make Search lose files it had found before.
        var removed = 0;
        var forgettable = look.StoppedEarly ? [] : existing.Keys.Where(id => !seen.Contains(id));
        foreach (var missing in forgettable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var delete = connection.CreateCommand();
            delete.Transaction = sqliteTransaction;
            delete.CommandText = "DELETE FROM indexed_files WHERE root_id = $rootId AND file_id = $fileId;";
            delete.Parameters.AddWithValue("$rootId", rootKey);
            delete.Parameters.AddWithValue("$fileId", missing.ToString("D"));
            removed += await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var record = connection.CreateCommand())
        {
            record.Transaction = sqliteTransaction;
            record.CommandText = """
                INSERT INTO index_looks(root_id, looked_at_utc, stopped_early, deep_folders_skipped)
                VALUES ($rootId, $lookedAtUtc, $stoppedEarly, $deepFoldersSkipped)
                ON CONFLICT(root_id) DO UPDATE SET
                    looked_at_utc = excluded.looked_at_utc,
                    stopped_early = excluded.stopped_early,
                    deep_folders_skipped = excluded.deep_folders_skipped;
                """;
            record.Parameters.AddWithValue("$rootId", rootKey);
            record.Parameters.AddWithValue("$lookedAtUtc", look.LookedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            record.Parameters.AddWithValue("$stoppedEarly", look.StoppedEarly ? 1 : 0);
            record.Parameters.AddWithValue("$deepFoldersSkipped", look.DeepFoldersSkipped);
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new FileIndexSyncResult(added, updated, unchanged, removed);
    }

    public async Task<IReadOnlyList<IndexedFile>> ListForRootAsync(
        Guid rootId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT file_id, relative_path, kind, category, size_bytes,
                   created_at_utc, modified_at_utc, indexed_at_utc
            FROM indexed_files
            WHERE root_id = $rootId
            ORDER BY relative_path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$rootId", rootId.ToString("D"));

        var files = new List<IndexedFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(Read(rootId, reader));
        }

        return files.AsReadOnly();
    }

    public async Task<IReadOnlyList<IndexedFile>> SearchRootAsync(
        Guid rootId,
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The clause text is assembled from fixed fragments and every value is bound as a
        // parameter, so no part of a query string ever comes from user or AI input.
        var where = new StringBuilder("WHERE root_id = $rootId");
        command.Parameters.AddWithValue("$rootId", rootId.ToString("D"));

        if (query.PathContains is { } text)
        {
            where.Append(" AND relative_path LIKE $pathPattern ESCAPE '\\'");
            command.Parameters.AddWithValue("$pathPattern", $"%{EscapeLike(text)}%");
        }

        if (query.Extensions.Count > 0)
        {
            where.Append(" AND (");
            var index = 0;
            foreach (var extension in query.Extensions)
            {
                if (index > 0)
                {
                    where.Append(" OR ");
                }

                var name = $"$ext{index}";
                where.Append(CultureInfo.InvariantCulture, $"lower(relative_path) LIKE {name} ESCAPE '\\'");
                command.Parameters.AddWithValue(name, $"%{EscapeLike(extension)}");
                index++;
            }

            where.Append(')');
        }

        AppendEnumFilter(where, command, "category", query.Categories.Select(category => (int)category));
        AppendEnumFilter(where, command, "kind", query.Kinds.Select(kind => (int)kind));

        if (query.MinSizeBytes is { } minSize)
        {
            where.Append(" AND size_bytes >= $minSize");
            command.Parameters.AddWithValue("$minSize", minSize);
        }

        if (query.MaxSizeBytes is { } maxSize)
        {
            where.Append(" AND size_bytes <= $maxSize");
            command.Parameters.AddWithValue("$maxSize", maxSize);
        }

        // Stored timestamps keep whatever offset the file carried, so a raw string compare
        // would order "+02:00" against "+00:00" incorrectly. Normalizing both sides to UTC
        // through SQLite keeps the range honest without a schema change.
        if (query.ModifiedAfterUtc is { } after)
        {
            where.Append(" AND strftime('%Y-%m-%dT%H:%M:%SZ', modified_at_utc) >= $modifiedAfter");
            command.Parameters.AddWithValue("$modifiedAfter", FormatUtcBoundary(after));
        }

        if (query.ModifiedBeforeUtc is { } before)
        {
            where.Append(" AND strftime('%Y-%m-%dT%H:%M:%SZ', modified_at_utc) <= $modifiedBefore");
            command.Parameters.AddWithValue("$modifiedBefore", FormatUtcBoundary(before));
        }

        command.CommandText = $"""
            SELECT file_id, relative_path, kind, category, size_bytes,
                   created_at_utc, modified_at_utc, indexed_at_utc
            FROM indexed_files
            {where}
            ORDER BY relative_path COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", query.Limit);

        var files = new List<IndexedFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(Read(rootId, reader));
        }

        return files.AsReadOnly();
    }

    public async Task<FileIndexStatistics> GetStatisticsAsync(
        Guid rootId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(size_bytes), 0), MAX(indexed_at_utc),
                   (SELECT looked_at_utc FROM index_looks WHERE root_id = $rootId),
                   (SELECT stopped_early FROM index_looks WHERE root_id = $rootId),
                   (SELECT deep_folders_skipped FROM index_looks WHERE root_id = $rootId)
            FROM indexed_files WHERE root_id = $rootId;
            """;
        command.Parameters.AddWithValue("$rootId", rootId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return FileIndexStatistics.Empty;
        }

        // The look is reported even when it found no files, so an empty folder still reads as
        // "checked just now" rather than "never checked".
        var count = reader.GetInt32(0);
        DateTimeOffset? lookedAt = reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3));
        var stoppedEarly = !reader.IsDBNull(4) && reader.GetInt64(4) != 0;
        var deepFoldersSkipped = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
        return new FileIndexStatistics(
            count,
            count == 0 ? 0 : reader.GetInt64(1),
            count == 0 ? null : ParseTimestamp(reader.GetString(2)),
            lookedAt,
            stoppedEarly,
            deepFoldersSkipped);
    }

    public async Task<IReadOnlyList<SizeGroup>> GetSizeCountsAsync(
        Guid rootId,
        long minimumSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumSizeBytes);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Grouping happens in SQL, so this stays cheap on a large folder and reads only the
        // size column. No file is opened, which is what lets it run under a metadata-only
        // authorization.
        //
        // Sizes that occur only once here are deliberately still returned. A file copied
        // into a second connected folder appears once in each, and filtering to counts
        // above one per root would hide exactly that case. The caller merges across roots
        // before deciding what repeats. Rows are bounded by the number of distinct sizes.
        command.CommandText = """
            SELECT size_bytes, COUNT(*)
            FROM indexed_files
            WHERE root_id = $rootId AND size_bytes >= $minimumSize
            GROUP BY size_bytes
            ORDER BY size_bytes DESC;
            """;
        command.Parameters.AddWithValue("$rootId", rootId.ToString("D"));
        command.Parameters.AddWithValue("$minimumSize", minimumSizeBytes);

        var groups = new List<SizeGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new SizeGroup(reader.GetInt64(0), reader.GetInt32(1)));
        }

        return groups.AsReadOnly();
    }

    public async Task<RootStorageSummary> SummarizeRootAsync(
        Guid rootId,
        DateTimeOffset unchangedSinceUtc,
        int largestFileCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(largestFileCount);

        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var rootKey = rootId.ToString("D");

        // Counting and summing happen in SQL so a folder with a hundred thousand remembered
        // files costs about the same to summarize as one with ten.
        var categories = new List<CategoryUsage>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT category, COUNT(*), COALESCE(SUM(size_bytes), 0)
                FROM indexed_files
                WHERE root_id = $rootId
                GROUP BY category
                ORDER BY SUM(size_bytes) DESC;
                """;
            command.Parameters.AddWithValue("$rootId", rootKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                categories.Add(new CategoryUsage(
                    (FileCategory)reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt64(2)));
            }
        }

        var largest = new List<IndexedFile>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT file_id, relative_path, kind, category, size_bytes,
                       created_at_utc, modified_at_utc, indexed_at_utc
                FROM indexed_files
                WHERE root_id = $rootId
                ORDER BY size_bytes DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$rootId", rootKey);
            command.Parameters.AddWithValue("$limit", largestFileCount);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                largest.Add(Read(rootId, reader));
            }
        }

        // Normalized to UTC for the same reason search range filters are: stored timestamps
        // keep whatever offset the file carried, so a raw string compare would misorder them.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(size_bytes), 0)
                FROM indexed_files
                WHERE root_id = $rootId
                  AND strftime('%Y-%m-%dT%H:%M:%SZ', modified_at_utc) < $unchangedSince;
                """;
            command.Parameters.AddWithValue("$rootId", rootKey);
            command.Parameters.AddWithValue("$unchangedSince", FormatUtcBoundary(unchangedSinceUtc));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new RootStorageSummary(
                    categories.AsReadOnly(),
                    largest.AsReadOnly(),
                    reader.GetInt32(0),
                    reader.GetInt64(1))
                : new RootStorageSummary(categories.AsReadOnly(), largest.AsReadOnly(), 0, 0);
        }
    }

    public async Task ClearRootAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteStore.OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM indexed_files WHERE root_id = $rootId;
            DELETE FROM index_looks WHERE root_id = $rootId;
            """;
        command.Parameters.AddWithValue("$rootId", rootId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<Guid, IndexedFile>> ReadExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid rootId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT file_id, relative_path, kind, category, size_bytes,
                   created_at_utc, modified_at_utc, indexed_at_utc
            FROM indexed_files WHERE root_id = $rootId;
            """;
        command.Parameters.AddWithValue("$rootId", rootId.ToString("D"));

        var existing = new Dictionary<Guid, IndexedFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var file = Read(rootId, reader);
            existing[file.FileId] = file;
        }

        return existing;
    }

    private static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string rootKey,
        IndexedFile file,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO indexed_files(
                root_id, file_id, relative_path, name, extension, kind, category,
                size_bytes, created_at_utc, modified_at_utc, indexed_at_utc)
            VALUES ($rootId, $fileId, $relativePath, $name, $extension, $kind, $category,
                    $sizeBytes, $createdAtUtc, $modifiedAtUtc, $indexedAtUtc)
            ON CONFLICT(root_id, file_id) DO UPDATE SET
                relative_path = excluded.relative_path,
                name = excluded.name,
                extension = excluded.extension,
                kind = excluded.kind,
                category = excluded.category,
                size_bytes = excluded.size_bytes,
                created_at_utc = excluded.created_at_utc,
                modified_at_utc = excluded.modified_at_utc,
                indexed_at_utc = excluded.indexed_at_utc;
            """;
        command.Parameters.AddWithValue("$rootId", rootKey);
        command.Parameters.AddWithValue("$fileId", file.FileId.ToString("D"));
        command.Parameters.AddWithValue("$relativePath", file.RelativePath);
        command.Parameters.AddWithValue("$name", file.Name);
        command.Parameters.AddWithValue("$extension", file.Extension);
        command.Parameters.AddWithValue("$kind", (int)file.Kind);
        command.Parameters.AddWithValue("$category", (int)file.Category);
        command.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
        command.Parameters.AddWithValue("$createdAtUtc", file.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$modifiedAtUtc", file.ModifiedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$indexedAtUtc", file.IndexedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IndexedFile Read(Guid rootId, SqliteDataReader reader) => new(
        rootId,
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        (FileKind)reader.GetInt32(2),
        (FileCategory)reader.GetInt32(3),
        reader.GetInt64(4),
        ParseTimestamp(reader.GetString(5)),
        ParseTimestamp(reader.GetString(6)),
        ParseTimestamp(reader.GetString(7)));

    /// <summary>
    /// Adds an <c>IN</c> filter for one integer column, binding each value separately.
    /// Does nothing when the caller asked for no values, which means "any".
    /// </summary>
    private static void AppendEnumFilter(
        StringBuilder where,
        SqliteCommand command,
        string column,
        IEnumerable<int> values)
    {
        var index = 0;
        var names = new List<string>();
        foreach (var value in values)
        {
            var name = $"${column}{index}";
            names.Add(name);
            command.Parameters.AddWithValue(name, value);
            index++;
        }

        if (names.Count > 0)
        {
            where.Append(CultureInfo.InvariantCulture, $" AND {column} IN ({string.Join(", ", names)})");
        }
    }

    /// <summary>
    /// Neutralizes the LIKE wildcards so a search for "report_final" cannot silently widen
    /// into a pattern match. Paired with <c>ESCAPE '\'</c> in every LIKE clause.
    /// </summary>
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Matches the shape produced by the strftime call used on the stored column.</summary>
    private static string FormatUtcBoundary(DateTimeOffset moment) => moment
        .ToUniversalTime()
        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);
}
