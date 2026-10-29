using FlushedYo.Core.Store;
using static FlushedYo.Core.Tests.Store.AccountLedgerTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Account-store validation, cancellation, rollback, and SQL parameterization.</summary>
public sealed class SqliteAccountLedgerStoreValidationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ImportWithTransactionFromDifferentAccount_ThrowsWithoutWritingAnything()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var mismatch = AccountTx(account: Account(accountKey: "other"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ImportAsync(Import(account, [mismatch]), Token));

        Assert.Contains("different account", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM accounts;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_transactions;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_imports;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_progress;", Token));
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsForEveryOperationAndLeavesCommittedStateUnchanged()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var original = AccountTx(account: account, externalId: "original");
        var originalResult = await store.ImportAsync(Import(account, [original]), Token);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ImportAsync(Import(account, [AccountTx(account: account, externalId: "new")]), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetTransactionsAsync(account, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetImportHistoryAsync(account, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetImportHistoryAsync(account, 1, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetLatestSafeDateAsync(account, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetCashStateAsync(account, cts.Token));

        Assert.Equal([original], await store.GetTransactionsAsync(account, Token));
        Assert.Single(await store.GetImportHistoryAsync(account, Token));
        Assert.Equal(originalResult.LatestSafeDate, await store.GetLatestSafeDateAsync(account, Token));
        Assert.Equal(originalResult.CashState, await store.GetCashStateAsync(account, Token));
    }

    [Fact]
    public async Task CancellationAfterFirstInsertedRow_RollsBackEntireImport()
    {
        using var ledger = new TempLedger();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var insertedCallbacks = 0;
        var store = new SqliteAccountLedgerStore(ledger.Database, () =>
        {
            insertedCallbacks++;
            cts.Cancel();
        });
        var account = Account();
        var first = AccountTx(account: account, externalId: "first");
        var second = AccountTx(account: account, externalId: "second");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ImportAsync(Import(account, [first, second]), cts.Token));

        Assert.Equal(1, insertedCallbacks);
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM accounts;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_transactions;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_imports;", Token));
        Assert.Equal(0L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_progress;", Token));
    }

    [Fact]
    public async Task SqlInjectionAttempt_IsStoredLiterallyWithoutChangingSchema()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var transaction = AccountTx(
            account: account,
            externalId: "source'); DROP TABLE accounts;--",
            payee: "x'); DROP TABLE account_transactions;--");

        await store.ImportAsync(Import(account, [transaction]), Token);

        Assert.Equal([transaction], await store.GetTransactionsAsync(account, Token));
        Assert.Equal(1L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM accounts;", Token));
        Assert.Equal(1L, await ScalarAsync(ledger, "SELECT COUNT(*) FROM account_transactions;", Token));
    }
}
