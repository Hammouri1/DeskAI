using Microsoft.Data.Sqlite;

namespace DeskAI.Infrastructure.Persistence;

internal static class SqliteStore
{
    public static string CreateConnectionString(string databasePath) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Cache = SqliteCacheMode.Shared,
        Pooling = false,
    }.ToString();

    public static async Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(CreateConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
