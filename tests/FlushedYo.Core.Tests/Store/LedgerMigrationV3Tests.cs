using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Data.Sqlite;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Schema-v3 upgrade and legacy-data preservation.</summary>
public sealed class LedgerMigrationV3Tests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task VersionOneOrTwoDatabase_UpgradePreservesLegacyRowHashAndLeavesAccountUnassigned(int schemaVersion)
    {
        using var ledger = new TempLedger();
        var legacy = Tx(externalId: "legacy", amount: 5m, date: new DateOnly(2026, 1, 1), payee: "Test");
        var legacyHash = CanonicalTransactionHasher.ComputeHash(legacy);
        await using (var raw = ledger.OpenRawConnection())
        {
            foreach (var migration in LedgerMigrations.All.Take(schemaVersion))
            {
                await ExecuteAsync(raw, migration.Sql);
            }

            await ExecuteAsync(raw, schemaVersion == 1 ? "PRAGMA user_version = 1;" : "PRAGMA user_version = 2;");
            await InsertLegacyAsync(raw, legacy, legacyHash, "5.00", "2026-01-01");
        }

        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal(3, LedgerMigrations.LatestVersion);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.canonical_hash, t.institution, t.external_id, t.amount, t.date, t.payee, t.type, a.account_id
            FROM transactions AS t
            INNER JOIN legacy_account_assignments AS a ON a.transaction_id = t.id;
            """;
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(legacyHash, reader.GetString(0));
        Assert.Equal(legacy.Institution, reader.GetString(1));
        Assert.Equal(legacy.ExternalId, reader.GetString(2));
        Assert.Equal("5.00", reader.GetString(3));
        Assert.Equal("2026-01-01", reader.GetString(4));
        Assert.Equal(legacy.Payee, reader.GetString(5));
        Assert.Equal(legacy.Type, reader.GetString(6));
        Assert.True(reader.IsDBNull(7));
        Assert.Equal(legacyHash, CanonicalTransactionHasher.ComputeHash(legacy));
    }

    [Fact]
    public async Task VersionOneDatabaseWithMultipleLegacyRows_UpgradePreservesEveryRowAndUnassignedAssignment()
    {
        using var ledger = new TempLedger();
        var rows = new[]
        {
            (Transaction: Tx(externalId: "legacy-1", amount: 5m, date: new DateOnly(2026, 1, 1), payee: "First"), Amount: "5.00", Date: "2026-01-01"),
            (Transaction: Tx(externalId: "legacy-2", amount: -7.25m, date: new DateOnly(2025, 12, 31), payee: "Second"), Amount: "-7.250", Date: "2025-12-31"),
            (Transaction: Tx(externalId: "legacy-3", amount: 0.005m, date: new DateOnly(2026, 2, 28), payee: "Third"), Amount: "0.0050", Date: "2026-02-28"),
        };
        await using (var raw = ledger.OpenRawConnection())
        {
            await ExecuteAsync(raw, LedgerMigrations.All[0].Sql);
            await ExecuteAsync(raw, "PRAGMA user_version = 1;");
            foreach (var row in rows)
            {
                await InsertLegacyAsync(
                    raw,
                    row.Transaction,
                    CanonicalTransactionHasher.ComputeHash(row.Transaction),
                    row.Amount,
                    row.Date);
            }
        }

        await using var connection = await ledger.Database.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.canonical_hash, t.institution, t.external_id, t.amount, t.date, t.payee, t.type, a.account_id
            FROM transactions AS t
            INNER JOIN legacy_account_assignments AS a ON a.transaction_id = t.id
            ORDER BY t.id;
            """;
        await using var reader = await command.ExecuteReaderAsync(Token);
        foreach (var row in rows)
        {
            Assert.True(await reader.ReadAsync(Token));
            Assert.Equal(CanonicalTransactionHasher.ComputeHash(row.Transaction), reader.GetString(0));
            Assert.Equal(row.Transaction.Institution, reader.GetString(1));
            Assert.Equal(row.Transaction.ExternalId, reader.GetString(2));
            Assert.Equal(row.Amount, reader.GetString(3));
            Assert.Equal(row.Date, reader.GetString(4));
            Assert.Equal(row.Transaction.Payee, reader.GetString(5));
            Assert.Equal(row.Transaction.Type, reader.GetString(6));
            Assert.True(reader.IsDBNull(7));
        }

        Assert.False(await reader.ReadAsync(Token));
    }

    [Fact]
    public async Task LegacyAndAccountWritesAfterV3Migration_LegacyInsertAndDuplicateHaveOneUnassignedRow()
    {
        using var ledger = new TempLedger();
        var account = AccountLedgerTestData.Account();
        var accountStore = AccountLedgerTestData.CreateStore(ledger);
        await accountStore.ImportAsync(
            AccountLedgerTestData.Import(account, [AccountLedgerTestData.AccountTx(account: account)]),
            Token);
        var legacyStore = CreateStore(ledger);
        var legacy = Tx(externalId: "legacy-after-v3");

        Assert.True(await legacyStore.RecordTransactionAsync(legacy, Audit(legacy), Token));
        Assert.False(await legacyStore.RecordTransactionAsync(legacy, Audit(legacy), Token));

        Assert.Equal(1L, await AccountLedgerTestData.ScalarAsync(ledger, "SELECT COUNT(*) FROM transactions;", Token));
        Assert.Equal(
            1L,
            await AccountLedgerTestData.ScalarAsync(ledger, "SELECT COUNT(*) FROM legacy_account_assignments;", Token));
        Assert.Equal(
            1L,
            await AccountLedgerTestData.ScalarAsync(
                ledger,
                "SELECT COUNT(*) FROM legacy_account_assignments WHERE account_id IS NULL;",
                Token));
        Assert.Equal(
            1L,
            await AccountLedgerTestData.ScalarAsync(ledger, "SELECT COUNT(*) FROM account_transactions;", Token));
    }

    [Fact]
    public async Task NewDatabase_CreatesAllAccountTablesWithoutChangingLegacyColumns()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal(
            5L,
            await AccountLedgerTestData.ScalarAsync(
                ledger,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN " +
                "('accounts', 'legacy_account_assignments', 'account_transactions', 'account_imports', 'account_progress');",
                Token));

        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('transactions') ORDER BY cid;";
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(
            ["id", "canonical_hash", "institution", "external_id", "amount", "date", "payee", "type", "plugin_version", "downloaded_at"],
            columns);
    }

    private static async Task InsertLegacyAsync(
        SqliteConnection connection,
        Transaction transaction,
        string hash,
        string amount,
        string date)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO transactions
                (canonical_hash, institution, external_id, amount, date, payee, type, plugin_version, downloaded_at)
            VALUES
                ($hash, $institution, $externalId, $amount, $date, $payee, $type, $pluginVersion, $downloadedAt);
            """;
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$institution", transaction.Institution);
        command.Parameters.AddWithValue("$externalId", transaction.ExternalId);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$date", date);
        command.Parameters.AddWithValue("$payee", transaction.Payee);
        command.Parameters.AddWithValue("$type", transaction.Type);
        command.Parameters.AddWithValue("$pluginVersion", "1.0.0");
        command.Parameters.AddWithValue("$downloadedAt", "2026-01-01T00:00:00.0000000Z");
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Token);
    }
}
