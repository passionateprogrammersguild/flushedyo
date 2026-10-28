using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tests.Plugin;
using FlushedYo.Core.Tests.Store;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>A temp ledger, real SQLite store, fixed clock, capturing logger, and tracker wired together.</summary>
internal sealed class TrackerHarness : IDisposable
{
    public TrackerHarness()
    {
        Ledger = new TempLedger();
        Store = new SqliteTransactionStore(Ledger.Database);
        Tracker = NewTracker(Store);
    }

    public TempLedger Ledger { get; }

    public SqliteTransactionStore Store { get; }

    public FixedTimeProvider Time { get; } = new();

    public CapturingLogger<TransactionTracker> Logger { get; } = new();

    public TransactionTracker Tracker { get; }

    public TransactionTracker NewTracker(ITransactionStore store) => new(store, Time, Logger);

    public Task<long> CountRowsAsync(CancellationToken cancellationToken) =>
        TransactionTestData.CountRowsAsync(Ledger, cancellationToken);

    public Task<List<TransactionTestData.StoredRow>> ReadRowsAsync(CancellationToken cancellationToken) =>
        TransactionTestData.ReadRowsAsync(Ledger, cancellationToken);

    public void Dispose() => Ledger.Dispose();
}

/// <summary>Delegates to an inner store and counts <see cref="HasTransactionAsync"/> calls.</summary>
internal sealed class HasCountingStore(ITransactionStore inner) : ITransactionStore
{
    public int HasCalls { get; private set; }

    public Task<bool> HasTransactionAsync(string canonicalHash, CancellationToken cancellationToken = default)
    {
        HasCalls++;
        return inner.HasTransactionAsync(canonicalHash, cancellationToken);
    }

    public Task<IEnumerable<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default) =>
        inner.GetTransactionsAsync(cancellationToken);

    public Task<bool> RecordTransactionAsync(Transaction transaction, TransactionAudit audit, CancellationToken cancellationToken = default) =>
        inner.RecordTransactionAsync(transaction, audit, cancellationToken);
}

/// <summary>Delegates to an inner store but throws on a chosen call, simulating a mid-batch failure.</summary>
internal sealed class FailingStore(ITransactionStore inner, int failOnRecordCall) : ITransactionStore
{
    private int _recordCalls;

    public Task<bool> HasTransactionAsync(string canonicalHash, CancellationToken cancellationToken = default) =>
        inner.HasTransactionAsync(canonicalHash, cancellationToken);

    public Task<IEnumerable<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default) =>
        inner.GetTransactionsAsync(cancellationToken);

    public Task<bool> RecordTransactionAsync(Transaction transaction, TransactionAudit audit, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _recordCalls) == failOnRecordCall)
        {
            throw new InvalidOperationException("Simulated store failure.");
        }

        return inner.RecordTransactionAsync(transaction, audit, cancellationToken);
    }
}
