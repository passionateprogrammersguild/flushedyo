using FlushedYo.Core.Models;
using static FlushedYo.Core.Tests.Store.AccountLedgerTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Account-scoped persistence, inclusive boundaries, history, and cash progress.</summary>
public sealed class SqliteAccountLedgerStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmptyAccount_QueriesReturnEmptyOrNullWithoutCreatingProgress()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();

        Assert.Empty(await store.GetTransactionsAsync(account, Token));
        Assert.Empty(await store.GetImportHistoryAsync(account, Token));
        Assert.Null(await store.GetLatestSafeDateAsync(account, Token));
        Assert.Null(await store.GetCashStateAsync(account, Token));
    }

    [Fact]
    public async Task Import_PersistsRowsHistoryAndProgressAcrossStoreInstances()
    {
        using var ledger = new TempLedger();
        var account = Account(institution: "  FIDELITY ", accountKey: " account-1 ");
        var transaction = AccountTx(
            account: account,
            amount: -12.30m,
            date: new DateOnly(2026, 1, 2),
            transactionInstitution: "fidelity");
        var importedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));
        var import = Import(account, [transaction], openingCashBalance: 100m, rejected: 2, importedAt: importedAt);

        var result = await CreateStore(ledger).ImportAsync(import, Token);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, result.Rejected);
        Assert.Equal(3, result.Total);
        Assert.Equal(transaction with { Account = Account() }, Assert.Single(result.NewlyInsertedTransactions));
        Assert.Null(result.LatestSafeDate);
        Assert.Equal(
            new CashState(
                CashStateStatus.ReconciliationNeeded,
                CashStateReason.AmbiguousTransaction,
                null,
                null,
                null),
            result.CashState);

        var second = CreateStore(ledger);
        Assert.Equal([transaction with { Account = Account() }], await second.GetTransactionsAsync(Account(), Token));
        Assert.Equal(result.LatestSafeDate, await second.GetLatestSafeDateAsync(Account(), Token));
        Assert.Equal(result.CashState, await second.GetCashStateAsync(Account(), Token));
        var history = Assert.Single(await second.GetImportHistoryAsync(Account(), Token));
        Assert.Equal("1.0.0", history.PluginVersion);
        Assert.Equal(importedAt.ToUniversalTime(), history.ImportedAt);
        Assert.Equal((1, 0, 2, 3), (history.Inserted, history.Skipped, history.Rejected, history.Total));
        Assert.Equal(result.LatestSafeDate, history.LatestSafeDate);
        Assert.Equal(result.CashState, history.CashState);
    }

    [Fact]
    public async Task EquivalentTransactions_InMultipleAccountsAndInstitutions_RemainScopedAndDoNotCollide()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var firstAccount = Account(accountKey: "shared-key");
        var secondAccount = Account(accountKey: "account-2");
        var otherInstitution = Account(institution: "other", accountKey: "shared-key");
        var first = AccountTx(account: firstAccount);
        var second = AccountTx(account: secondAccount);
        var third = AccountTx(account: otherInstitution, transactionInstitution: "other");

        await store.ImportAsync(Import(firstAccount, [first]), Token);
        await store.ImportAsync(Import(secondAccount, [second]), Token);
        await store.ImportAsync(Import(otherInstitution, [third]), Token);

        Assert.Equal([first], await store.GetTransactionsAsync(firstAccount, Token));
        Assert.Equal([second], await store.GetTransactionsAsync(secondAccount, Token));
        Assert.Equal([third], await store.GetTransactionsAsync(otherInstitution, Token));
        Assert.Single(await store.GetImportHistoryAsync(firstAccount, Token));
        Assert.Single(await store.GetImportHistoryAsync(secondAccount, Token));
        Assert.Single(await store.GetImportHistoryAsync(otherInstitution, Token));
        Assert.Equal(87.70m, (await store.GetCashStateAsync(firstAccount, Token))!.CashBalance);
        Assert.Equal(87.70m, (await store.GetCashStateAsync(secondAccount, Token))!.CashBalance);
        Assert.Equal(87.70m, (await store.GetCashStateAsync(otherInstitution, Token))!.CashBalance);
    }

    [Fact]
    public async Task ReimportWithLateAndSameDayRecords_InsertsOnlyNewRowsAndKeepsInclusiveWatermark()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var sameDay = AccountTx(account: account, externalId: "same-day", amount: 10m, date: new DateOnly(2026, 1, 3));
        var earlier = AccountTx(account: account, externalId: "earlier", amount: -2m, date: new DateOnly(2026, 1, 2));
        await store.ImportAsync(Import(account, [sameDay, earlier]), Token);

        var late = AccountTx(account: account, externalId: "late", amount: 5m, date: new DateOnly(2026, 1, 1));
        var anotherSameDay = AccountTx(
            account: account,
            externalId: "another-same-day",
            amount: -1m,
            date: new DateOnly(2026, 1, 3));
        var overlap = Import(account, [sameDay, late, anotherSameDay], openingCashBalance: null);

        var result = await store.ImportAsync(overlap, Token);

        Assert.Equal(2, result.Inserted);
        Assert.Equal(1, result.Skipped);
        Assert.Equal([late, anotherSameDay], result.NewlyInsertedTransactions);
        Assert.Equal(new DateOnly(2026, 1, 3), result.LatestSafeDate);
        Assert.Equal(new CashState(CashStateStatus.Known, CashStateReason.None, 108m, 112m, 4m), result.CashState);

        var replay = await store.ImportAsync(overlap, Token);
        Assert.Equal(0, replay.Inserted);
        Assert.Equal(3, replay.Skipped);
        Assert.Empty(replay.NewlyInsertedTransactions);
        Assert.Equal(result.CashState, replay.CashState);
        Assert.Equal(4, (await store.GetTransactionsAsync(account, Token)).Count);
        Assert.Equal(new DateOnly(2026, 1, 3), await store.GetLatestSafeDateAsync(account, Token));
    }

    [Fact]
    public async Task DuplicateOnlyImport_WithExplicitBaseline_ResynchronizesWithoutDoubleApplyingRows()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var transaction = AccountTx(account: account, amount: -10m);
        await store.ImportAsync(Import(account, [transaction], openingCashBalance: 100m), Token);

        var resynchronized = await store.ImportAsync(
            Import(account, [transaction], openingCashBalance: 250m),
            Token);

        Assert.Equal(0, resynchronized.Inserted);
        Assert.Equal(1, resynchronized.Skipped);
        Assert.Equal(
            new CashState(CashStateStatus.Known, CashStateReason.None, 250m, 250m, 0m),
            resynchronized.CashState);
        Assert.Equal(resynchronized.CashState, await store.GetCashStateAsync(account, Token));
        Assert.Single(await store.GetTransactionsAsync(account, Token));
        Assert.Equal(2, (await store.GetImportHistoryAsync(account, Token)).Count);

        var withoutBaseline = await store.ImportAsync(
            Import(account, [transaction], openingCashBalance: null),
            Token);
        Assert.Equal(resynchronized.CashState, withoutBaseline.CashState);
    }

    [Fact]
    public async Task MissingBaselineAndUnresolvedActivity_PersistExplicitStatesWithoutValues()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var first = AccountTx(account: account, externalId: "first", amount: 10m);

        var unknown = await store.ImportAsync(Import(account, [first], openingCashBalance: null), Token);
        Assert.Equal(CashStateStatus.Unknown, unknown.CashState.Status);
        Assert.Equal(CashStateReason.MissingBaseline, unknown.CashState.Reason);
        Assert.Null(unknown.CashState.CashBalance);
        Assert.Null(unknown.CashState.Difference);

        var correction = AccountTx(
            account: account,
            externalId: "correction",
            amount: -5m,
            cashImpact: CashImpact.Correction);
        var needsReconciliation = await store.ImportAsync(
            Import(account, [correction], openingCashBalance: null),
            Token);
        Assert.Equal(CashStateStatus.ReconciliationNeeded, needsReconciliation.CashState.Status);
        Assert.Equal(CashStateReason.Correction, needsReconciliation.CashState.Reason);
        Assert.Null(needsReconciliation.CashState.OpeningCashBalance);
        Assert.Equal(needsReconciliation.CashState, await store.GetCashStateAsync(account, Token));
    }

    [Fact]
    public async Task RejectedImport_DoesNotAdvanceExistingSafeDateAndRecordsHistoryAtomically()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var established = AccountTx(account: account, externalId: "established", date: new DateOnly(2026, 1, 1));
        await store.ImportAsync(Import(account, [established]), Token);
        var later = AccountTx(account: account, externalId: "later", date: new DateOnly(2026, 1, 2));

        var result = await store.ImportAsync(Import(account, [later], rejected: 1), Token);

        Assert.Equal(new DateOnly(2026, 1, 1), result.LatestSafeDate);
        Assert.Equal(new DateOnly(2026, 1, 1), await store.GetLatestSafeDateAsync(account, Token));
        Assert.Equal(CashStateStatus.ReconciliationNeeded, result.CashState.Status);
        var history = await store.GetImportHistoryAsync(account, Token);
        Assert.Equal(2, history.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), history[^1].LatestSafeDate);
        Assert.Equal(1, history[^1].Rejected);
        Assert.Equal(2, (await store.GetTransactionsAsync(account, Token)).Count);
    }

    [Fact]
    public async Task DecimalOverflow_PersistsReconciliationNeededRatherThanThrowing()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var transaction = AccountTx(account: account, amount: 1m);

        var result = await store.ImportAsync(
            Import(account, [transaction], openingCashBalance: decimal.MaxValue),
            Token);

        Assert.Equal(CashStateStatus.ReconciliationNeeded, result.CashState.Status);
        Assert.Equal(CashStateReason.ArithmeticOverflow, result.CashState.Reason);
        Assert.Null(result.CashState.OpeningCashBalance);
        Assert.Null(result.CashState.CashBalance);
        Assert.Null(result.CashState.Difference);
        Assert.Equal(result.CashState, await store.GetCashStateAsync(account, Token));
        Assert.Equal(result.CashState, Assert.Single(await store.GetImportHistoryAsync(account, Token)).CashState);
    }
}
