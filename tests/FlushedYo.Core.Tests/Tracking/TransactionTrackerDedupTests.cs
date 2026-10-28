using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>
/// Failure-injection and concurrency scenarios (spec: transaction_tracker_dedup).
/// Sequential idempotency invariants are covered as properties in
/// <c>FlushedYo.Core.PropertyTests.Tracking.TransactionTrackerIdempotencyProperties</c>.
/// </summary>
public sealed class TransactionTrackerDedupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RerunAfterPartialFailure_InsertsOnlyTheRemainder()
    {
        using var harness = new TrackerHarness();
        var batch = Enumerable.Range(0, 6).Select(index => Tx(externalId: "id-" + index)).ToArray();
        var failing = harness.NewTracker(new FailingStore(harness.Store, failOnRecordCall: 4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.TrackAsync(batch, "1.0.0", Token));
        var rerun = await harness.Tracker.TrackAsync(batch, "1.0.0", Token);

        Assert.Equal(new(3, 3, 0), rerun);
        Assert.Equal(6, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task TwoTrackersSharingOneLedger_CannotCreateDuplicates()
    {
        using var harness = new TrackerHarness();

        // Create and migrate the database first: concurrent first-time migration by separate
        // processes is a documented, accepted race (see LedgerMigrations).
        await harness.CountRowsAsync(Token);

        var otherLedger = new LedgerDatabase(harness.Ledger.DatabasePath, NullLogger<LedgerDatabase>.Instance);
        var otherTracker = harness.NewTracker(new SqliteTransactionStore(otherLedger));
        Transaction[] batch = [Tx(externalId: "A"), Tx(externalId: "B"), Tx(externalId: "C")];

        var results = await Task.WhenAll(
            harness.Tracker.TrackAsync(batch, "1.0.0", Token),
            otherTracker.TrackAsync(batch, "1.0.0", Token));

        Assert.Equal(3, results.Sum(static result => result.Inserted));
        Assert.Equal(3, results.Sum(static result => result.Skipped));
        Assert.Equal(3, await harness.CountRowsAsync(Token));
    }
}
