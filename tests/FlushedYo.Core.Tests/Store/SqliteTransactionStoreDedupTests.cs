using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>
/// Concurrency scenarios (spec: sqlite_transaction_store).
/// Sequential dedup invariants are covered as properties in
/// <c>FlushedYo.Core.PropertyTests.Tracking.TransactionTrackerIdempotencyProperties</c>.
/// </summary>
public sealed class SqliteTransactionStoreDedupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentRecordsOfTheSameTransaction_InsertExactlyOnce()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var transaction = Tx();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => store.RecordTransactionAsync(transaction, Audit(transaction), Token)));

        Assert.Equal(1, results.Count(static inserted => inserted));
        Assert.Equal(15, results.Count(static inserted => !inserted));
        Assert.Equal(1, await CountRowsAsync(ledger, Token));
    }

    [Fact]
    public async Task ConcurrentRecordsOfDistinctTransactions_AllInsert()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var transactions = Enumerable.Range(0, 16).Select(index => Tx(externalId: "id-" + index)).ToArray();

        var results = await Task.WhenAll(
            transactions.Select(transaction => store.RecordTransactionAsync(transaction, Audit(transaction), Token)));

        Assert.All(results, Assert.True);
        Assert.Equal(16, await CountRowsAsync(ledger, Token));
    }
}
