using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FlushedYo.Core.Store;

/// <summary>
/// Ordered, in-code schema migrations for the ledger. The applied version is tracked with
/// <c>PRAGMA user_version</c>.
/// </summary>
/// <remarks>
/// Two processes migrating a brand-new database at the same instant can race and one will fail.
/// flushedyo is a single-user tool, so this is accepted.
/// </remarks>
internal static class LedgerMigrations
{
    internal sealed record Migration(int Version, string Sql);

    internal static IReadOnlyList<Migration> All { get; } =
    [
        new(
            1,
            """
            CREATE TABLE transactions (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                canonical_hash TEXT    NOT NULL UNIQUE,
                institution    TEXT    NOT NULL,
                external_id    TEXT    NOT NULL,
                amount         TEXT    NOT NULL,
                date           TEXT    NOT NULL,
                payee          TEXT    NOT NULL,
                type           TEXT    NOT NULL,
                plugin_version TEXT    NOT NULL,
                downloaded_at  TEXT    NOT NULL
            );
            CREATE INDEX ix_transactions_institution ON transactions (institution);
            """),
        new(
            2,
            """
            CREATE TABLE download_runs (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                plugin         TEXT    NOT NULL,
                plugin_version TEXT    NOT NULL,
                started_at     TEXT    NOT NULL,
                finished_at    TEXT    NOT NULL,
                outcome        TEXT    NOT NULL CHECK (outcome IN ('succeeded', 'failed')),
                inserted       INTEGER NOT NULL CHECK (inserted >= 0),
                skipped        INTEGER NOT NULL CHECK (skipped >= 0),
                rejected       INTEGER NOT NULL CHECK (rejected >= 0),
                error          TEXT
            );
            CREATE INDEX ix_download_runs_plugin ON download_runs (plugin, id);
            """),
    ];

    internal static int LatestVersion => All[^1].Version;

    /// <summary>Applies every migration newer than the database's <c>user_version</c>.</summary>
    /// <param name="connection">An open connection to the ledger database.</param>
    /// <exception cref="LedgerException">The database is newer than this build, or a migration failed.</exception>
    internal static void Apply(SqliteConnection connection)
    {
        var current = ReadUserVersion(connection);
        if (current > LatestVersion)
        {
            throw new LedgerException(
                $"The ledger database schema version {current} is newer than this build supports ({LatestVersion}). Upgrade flushedyo.");
        }

        foreach (var migration in All.Where(candidate => candidate.Version > current))
        {
            try
            {
                using var transaction = connection.BeginTransaction();
                Execute(connection, transaction, migration.Sql);

                // The version is a compile-time integer, so formatting it into the statement is safe.
                Execute(connection, transaction, string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {migration.Version};"));
                transaction.Commit();
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                throw new LedgerException($"Failed to apply ledger migration {migration.Version}.", ex);
            }
        }
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
