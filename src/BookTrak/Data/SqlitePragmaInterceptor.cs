using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookTrak.Data;

/// <summary>
/// WAL mode + busy_timeout must be set on every opened connection (busy_timeout is a
/// per-connection pragma; journal_mode persists in the file after the first set but costs
/// nothing to repeat). Required alongside <c>IDbContextFactory</c>'s short-lived contexts so
/// concurrent Blazor circuit reads don't throw "database is locked" against a writer.
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }
}
