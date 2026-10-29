using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tests.Plugin;
using FlushedYo.Core.Tests.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging;
using static FlushedYo.Core.Tests.Store.AccountLedgerTestData;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>Importer validation, rejection reporting, cancellation, and single-forwarding guarantees.</summary>
public sealed class AccountTransactionImporterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InvalidNullAndMismatchedRows_AreRejectedAndReportedWithoutAdvancingSafeDate()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var logger = new CapturingLogger<AccountTransactionImporter>();
        var importer = new AccountTransactionImporter(store, new FixedTimeProvider(), logger);
        var account = Account();
        var valid = AccountTx(account: account, externalId: "valid", date: new DateOnly(2026, 1, 3));
        var mismatch = AccountTx(account: Account(accountKey: "other"), externalId: "mismatch");
        var invalid = AccountTx(account: account, externalId: " ");

        var result = await importer.ImportAsync(
            account,
            [valid, mismatch, invalid, null!],
            "2.0.0",
            openingCashBalance: 100m,
            Token);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(3, result.Rejected);
        Assert.Equal(4, result.Total);
        Assert.Equal([valid], result.NewlyInsertedTransactions);
        Assert.Null(result.LatestSafeDate);
        Assert.Equal(CashStateStatus.ReconciliationNeeded, result.CashState.Status);
        Assert.Equal(CashStateReason.AmbiguousTransaction, result.CashState.Reason);
        Assert.Equal([valid], await store.GetTransactionsAsync(account, Token));
        var history = Assert.Single(await store.GetImportHistoryAsync(account, Token));
        Assert.Equal((1, 0, 3, 4), (history.Inserted, history.Skipped, history.Rejected, history.Total));
        Assert.Null(history.LatestSafeDate);
        Assert.Equal(3, logger.Entries.Count(static entry => entry.Level == LogLevel.Warning));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                entry.Message.Contains("1 inserted, 0 skipped, 3 rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidSequence_IsEnumeratedOnceAndForwardedToStoreExactlyOnce()
    {
        var store = new RecordingStore();
        var time = new FixedTimeProvider();
        var importer = new AccountTransactionImporter(store, time, new CapturingLogger<AccountTransactionImporter>());
        var account = Account(institution: "  FIDELITY ", accountKey: " account-1 ");
        var transaction = AccountTx(account: account);
        var sequence = new CountingSequence([transaction]);

        await importer.ImportAsync(account, sequence, "2.3.4", openingCashBalance: 50m, Token);

        Assert.Equal(1, sequence.EnumerationCount);
        var forwarded = Assert.Single(store.Imports);
        Assert.Same(account, forwarded.Account);
        Assert.Equal([transaction], forwarded.Transactions);
        Assert.Equal("2.3.4", forwarded.PluginVersion);
        Assert.Equal(time.Now, forwarded.ImportedAt);
        Assert.Equal(50m, forwarded.OpeningCashBalance);
        Assert.Equal(0, forwarded.Rejected);
    }

    [Fact]
    public async Task PreCancelledToken_DoesNotEnumerateOrCallStore()
    {
        var store = new RecordingStore();
        var importer = new AccountTransactionImporter(store, new FixedTimeProvider(), new CapturingLogger<AccountTransactionImporter>());
        var sequence = new CountingSequence([AccountTx()]);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            importer.ImportAsync(Account(), sequence, "1.0.0", cancellationToken: cts.Token));

        Assert.Equal(0, sequence.EnumerationCount);
        Assert.Empty(store.Imports);
    }

    [Fact]
    public async Task CancellationDuringEnumeration_DoesNotForwardPartialBatch()
    {
        var store = new RecordingStore();
        var importer = new AccountTransactionImporter(store, new FixedTimeProvider(), new CapturingLogger<AccountTransactionImporter>());
        var account = Account();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var enumerationCount = 0;

        IEnumerable<AccountTransaction> Batch()
        {
            enumerationCount++;
            yield return AccountTx(account: account, externalId: "first");
            cts.Cancel();
            yield return AccountTx(account: account, externalId: "second");
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            importer.ImportAsync(account, Batch(), "1.0.0", cancellationToken: cts.Token));

        Assert.Equal(1, enumerationCount);
        Assert.Empty(store.Imports);
    }

    private sealed class CountingSequence(IReadOnlyList<AccountTransaction> items) : IEnumerable<AccountTransaction>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<AccountTransaction> GetEnumerator()
        {
            EnumerationCount++;
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingStore : IAccountLedgerStore
    {
        public List<AccountImport> Imports { get; } = [];

        public Task<AccountImportResult> ImportAsync(AccountImport import, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Imports.Add(import);
            var inserted = import.Transactions.ToArray();
            return Task.FromResult(new AccountImportResult
            {
                Inserted = inserted.Length,
                Skipped = 0,
                Rejected = import.Rejected,
                NewlyInsertedTransactions = inserted,
                LatestSafeDate = null,
                CashState = new CashState(CashStateStatus.Unknown, CashStateReason.MissingBaseline, null, null, null),
            });
        }

        public Task<IReadOnlyList<AccountImportRecord>> GetImportHistoryAsync(
            Account account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccountImportRecord>> GetImportHistoryAsync(
            Account account,
            int maximumRecords,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccountTransaction>> GetTransactionsAsync(
            Account account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DateOnly?> GetLatestSafeDateAsync(Account account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CashState?> GetCashStateAsync(Account account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
