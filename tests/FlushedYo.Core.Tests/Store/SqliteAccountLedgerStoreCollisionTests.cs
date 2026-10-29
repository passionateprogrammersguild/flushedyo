using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;
using static FlushedYo.Core.Tests.Store.AccountLedgerTestData;
using static FlushedYo.Core.Tests.Store.SqliteErrorCodes;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Ambiguous identities, rollback, unrelated constraints, and concurrent deduplication.</summary>
public sealed class SqliteAccountLedgerStoreCollisionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SameSourceWithDifferentPayload_FailsClosedAndRollsBackWholeImport()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var first = AccountTx(account: account, externalId: "first", amount: 10m);
        var ambiguous = AccountTx(account: account, externalId: " shared-source ", amount: 20m);
        var conflicting = AccountTx(account: account, externalId: "shared-source", amount: 21m);

        var exception = await Assert.ThrowsAsync<LedgerException>(() =>
            store.ImportAsync(Import(account, [first, ambiguous, conflicting]), Token));

        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shared-source", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await store.GetTransactionsAsync(account, Token));
        Assert.Empty(await store.GetImportHistoryAsync(account, Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM accounts;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_transactions;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_imports;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_progress;", Token));
    }

    [Fact]
    public async Task DuplicateWithChangedCashClassification_FailsClosedWithoutReplacingOriginalState()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var original = AccountTx(account: account, cashImpact: CashImpact.CashAffecting);
        var originalResult = await store.ImportAsync(Import(account, [original]), Token);
        var changed = original with { CashImpact = CashImpact.Unknown };

        await Assert.ThrowsAsync<LedgerException>(() => store.ImportAsync(Import(account, [changed]), Token));

        Assert.Equal([original], await store.GetTransactionsAsync(account, Token));
        Assert.Equal(originalResult.CashState, await store.GetCashStateAsync(account, Token));
        Assert.Single(await store.GetImportHistoryAsync(account, Token));
    }

    [Fact]
    public async Task UnrelatedUniqueConstraint_IsNotMisreportedAsAmbiguousSourceAndRollsBack()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        await store.ImportAsync(Import(account, [AccountTx(account: account, externalId: "first")]), Token);
        await using (var connection = await ledger.Database.OpenAsync(Token))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE UNIQUE INDEX test_unique_plugin_version ON account_transactions (plugin_version);";
            await command.ExecuteNonQueryAsync(Token);
        }

        var second = AccountTx(account: account, externalId: "second", amount: 99m);
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            store.ImportAsync(Import(account, [second]), Token));

        Assert.Equal(Constraint, exception.SqliteErrorCode);
        Assert.Equal(ConstraintUnique, exception.SqliteExtendedErrorCode);
        Assert.Single(await store.GetTransactionsAsync(account, Token));
        Assert.Single(await store.GetImportHistoryAsync(account, Token));
    }

    /// <summary>
    /// Rows with other source keys already committed must not turn an unrelated UNIQUE violation (
    /// failing insert's own source key has no row) into an ambiguous-source report, and the import rolls back.
    /// </summary>
    [Fact]
    public async Task UnrelatedUniqueConstraint_WithPersistedOtherSources_IsNotAmbiguousAndRollsBack()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        await store.ImportAsync(Import(account, [AccountTx(account: account, externalId: "first")]), Token);
        await using (var connection = await ledger.Database.OpenAsync(Token))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE UNIQUE INDEX test_unique_plugin_version ON account_transactions (plugin_version);";
            await command.ExecuteNonQueryAsync(Token);
        }

        var fresh = AccountTx(account: account, externalId: "fresh", amount: 7m);
        var second = AccountTx(account: account, externalId: "second", amount: 99m);
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            store.ImportAsync(Import(account, [fresh, second]), Token));

        Assert.Equal(Constraint, exception.SqliteErrorCode);
        Assert.Equal(ConstraintUnique, exception.SqliteExtendedErrorCode);
        Assert.Single(await store.GetTransactionsAsync(account, Token));
        Assert.Single(await store.GetImportHistoryAsync(account, Token));
    }

    [Fact]
    public async Task UnrelatedConstraint_WithExistingSource_IsNotMisreportedAsAmbiguousSource()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var original = AccountTx(account: account, externalId: "shared-source");
        await store.ImportAsync(Import(account, [original]), Token);
        await using (var connection = await ledger.Database.OpenAsync(Token))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER test_unrelated_constraint
                BEFORE INSERT ON account_transactions
                BEGIN
                    SELECT RAISE(ABORT, 'test constraint');
                END;
                """;
            await command.ExecuteNonQueryAsync(Token);
        }

        var changed = original with { Transaction = original.Transaction with { Amount = 99m } };
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            store.ImportAsync(Import(account, [changed]), Token));

        // The trigger's extended code is incidental; only the primary constraint code is contractual.
        Assert.Equal(Constraint, exception.SqliteErrorCode);
        Assert.Equal([original], await store.GetTransactionsAsync(account, Token));
        Assert.Single(await store.GetImportHistoryAsync(account, Token));
    }

    [Fact]
    public async Task ConcurrentDuplicateImports_InsertExactlyOnceAndRecordEverySuccessfulAttempt()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var import = Import(account, [AccountTx(account: account)], openingCashBalance: null);
        Assert.Empty(await store.GetTransactionsAsync(account, Token));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => store.ImportAsync(import, Token)));

        Assert.Equal(1, results.Sum(static result => result.Inserted));
        Assert.Equal(15, results.Sum(static result => result.Skipped));
        Assert.Equal(1, results.Count(static result => result.NewlyInsertedTransactions.Count == 1));
        Assert.Single(await store.GetTransactionsAsync(account, Token));
        Assert.Equal(16, (await store.GetImportHistoryAsync(account, Token)).Count);
        Assert.Equal(CashStateStatus.Unknown, (await store.GetCashStateAsync(account, Token))!.Status);
    }

    [Fact]
    public async Task ConcurrentEquivalentImports_ForDifferentAccounts_DoNotCollide()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        Assert.Empty(await store.GetTransactionsAsync(Account(), Token));
        var imports = Enumerable.Range(0, 8).Select(index =>
        {
            var account = Account(accountKey: "account-" + index);
            return Import(account, [AccountTx(account: account)], openingCashBalance: null);
        }).ToArray();

        var results = await Task.WhenAll(imports.Select(import => store.ImportAsync(import, Token)));

        Assert.All(results, static result => Assert.Equal(1, result.Inserted));
        foreach (var import in imports)
        {
            Assert.Single(await store.GetTransactionsAsync(import.Account, Token));
        }
    }
}
