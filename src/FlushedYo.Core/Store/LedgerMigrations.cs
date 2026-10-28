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
        new(
            3,
            """
            CREATE TABLE accounts (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                institution TEXT    NOT NULL,
                account_key TEXT    NOT NULL,
                created_at  TEXT    NOT NULL,
                UNIQUE (institution, account_key)
            );
            CREATE TABLE legacy_account_assignments (
                transaction_id INTEGER PRIMARY KEY REFERENCES transactions (id),
                account_id     INTEGER NULL REFERENCES accounts (id)
            );
            INSERT INTO legacy_account_assignments (transaction_id, account_id)
            SELECT id, NULL FROM transactions;
            CREATE TABLE account_transactions (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id     INTEGER NOT NULL REFERENCES accounts (id),
                dedup_hash     TEXT    NOT NULL UNIQUE,
                legacy_hash    TEXT    NOT NULL,
                institution    TEXT    NOT NULL,
                source_key     TEXT    NOT NULL,
                external_id    TEXT    NOT NULL,
                amount         TEXT    NOT NULL,
                date           TEXT    NOT NULL,
                payee          TEXT    NOT NULL,
                type           TEXT    NOT NULL,
                cash_impact    TEXT    NOT NULL CHECK (cash_impact IN ('cash', 'non-cash', 'unknown', 'correction', 'unsupported')),
                plugin_version TEXT    NOT NULL,
                downloaded_at  TEXT    NOT NULL,
                UNIQUE (account_id, source_key)
            );
            CREATE INDEX ix_account_transactions_account_date ON account_transactions (account_id, date, id);
            CREATE TABLE account_imports (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id          INTEGER NOT NULL REFERENCES accounts (id),
                plugin_version      TEXT    NOT NULL,
                imported_at         TEXT    NOT NULL,
                inserted            INTEGER NOT NULL CHECK (inserted >= 0),
                skipped             INTEGER NOT NULL CHECK (skipped >= 0),
                rejected            INTEGER NOT NULL CHECK (rejected >= 0),
                latest_safe_date    TEXT,
                cash_status         TEXT    NOT NULL CHECK (cash_status IN ('known', 'unknown', 'reconciliation-needed')),
                cash_reason         TEXT    NOT NULL CHECK (cash_reason IN ('none', 'missing-baseline', 'ambiguous-transaction', 'correction', 'unsupported-activity', 'arithmetic-overflow')),
                opening_cash_balance TEXT,
                cash_balance        TEXT,
                cash_difference     TEXT,
                CONSTRAINT ck_account_imports_cash_state CHECK (
                    (cash_status = 'known' AND cash_reason = 'none'
                        AND opening_cash_balance IS NOT NULL AND cash_balance IS NOT NULL AND cash_difference IS NOT NULL)
                    OR (cash_status = 'unknown' AND cash_reason = 'missing-baseline'
                        AND opening_cash_balance IS NULL AND cash_balance IS NULL AND cash_difference IS NULL)
                    OR (cash_status = 'reconciliation-needed'
                        AND cash_reason IN ('ambiguous-transaction', 'correction', 'unsupported-activity', 'arithmetic-overflow')
                        AND opening_cash_balance IS NULL AND cash_balance IS NULL AND cash_difference IS NULL)
                )
            );
            CREATE INDEX ix_account_imports_account ON account_imports (account_id, id);
            CREATE TABLE account_progress (
                account_id           INTEGER PRIMARY KEY REFERENCES accounts (id),
                last_import_id       INTEGER NOT NULL UNIQUE REFERENCES account_imports (id),
                latest_safe_date     TEXT,
                cash_status          TEXT    NOT NULL CHECK (cash_status IN ('known', 'unknown', 'reconciliation-needed')),
                cash_reason          TEXT    NOT NULL CHECK (cash_reason IN ('none', 'missing-baseline', 'ambiguous-transaction', 'correction', 'unsupported-activity', 'arithmetic-overflow')),
                opening_cash_balance TEXT,
                cash_balance         TEXT,
                cash_difference      TEXT,
                updated_at           TEXT    NOT NULL,
                CONSTRAINT ck_account_progress_cash_state CHECK (
                    (cash_status = 'known' AND cash_reason = 'none'
                        AND opening_cash_balance IS NOT NULL AND cash_balance IS NOT NULL AND cash_difference IS NOT NULL)
                    OR (cash_status = 'unknown' AND cash_reason = 'missing-baseline'
                        AND opening_cash_balance IS NULL AND cash_balance IS NULL AND cash_difference IS NULL)
                    OR (cash_status = 'reconciliation-needed'
                        AND cash_reason IN ('ambiguous-transaction', 'correction', 'unsupported-activity', 'arithmetic-overflow')
                        AND opening_cash_balance IS NULL AND cash_balance IS NULL AND cash_difference IS NULL)
                )
            );
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
