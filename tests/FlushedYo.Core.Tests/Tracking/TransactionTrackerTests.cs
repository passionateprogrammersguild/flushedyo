using FlushedYo.Core.Models;
using Microsoft.Extensions.Logging;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>Insert, audit, validation, and cancellation behavior (spec: transaction_tracker_dedup).</summary>
public sealed class TransactionTrackerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DistinctTransactions_AreAllInserted_InOrder()
    {
        using var harness = new TrackerHarness();
        Transaction[] batch = [Tx(externalId: "1"), Tx(externalId: "2"), Tx(externalId: "3")];

        var result = await harness.Tracker.TrackAsync(batch, "1.0.0", Token);

        Assert.Equal(new(3, 0, 0), result);
        Assert.Equal(3, result.Total);
        Assert.Equal(batch, await harness.Store.GetTransactionsAsync(Token));
    }

    [Fact]
    public async Task EmptyBatch_ReturnsZeroes()
    {
        using var harness = new TrackerHarness();

        var result = await harness.Tracker.TrackAsync([], "1.0.0", Token);

        Assert.Equal(new(0, 0, 0), result);
        Assert.Equal(0, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task AuditValues_AreStoredWithEachRow()
    {
        using var harness = new TrackerHarness();

        await harness.Tracker.TrackAsync([Tx(externalId: "1"), Tx(externalId: "2")], "2.5.1", Token);

        var rows = await harness.ReadRowsAsync(Token);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("2.5.1", row.PluginVersion);
            Assert.Equal("2026-03-04T05:06:07.0000000Z", row.DownloadedAt);
        });
    }

    [Fact]
    public async Task AllTransactionsInOneBatch_ShareOneTimestamp()
    {
        using var harness = new TrackerHarness();

        IEnumerable<Transaction> Batch()
        {
            yield return Tx(externalId: "1");
            harness.Time.Now = harness.Time.Now.AddDays(1);
            yield return Tx(externalId: "2");
        }

        await harness.Tracker.TrackAsync(Batch(), "1.0.0", Token);

        var rows = await harness.ReadRowsAsync(Token);
        Assert.Single(rows.Select(static row => row.DownloadedAt).Distinct());
        Assert.Equal("2026-03-04T05:06:07.0000000Z", rows[0].DownloadedAt);
    }

    [Fact]
    public async Task LaterBatch_UsesTheNewTimeForNewRowsOnly()
    {
        using var harness = new TrackerHarness();
        var first = Tx(externalId: "1");
        var second = Tx(externalId: "2");
        await harness.Tracker.TrackAsync([first], "1.0.0", Token);

        harness.Time.Now = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);
        await harness.Tracker.TrackAsync([first, second], "1.0.0", Token);

        var rows = await harness.ReadRowsAsync(Token);
        Assert.Equal("2026-03-04T05:06:07.0000000Z", rows[0].DownloadedAt);
        Assert.Equal("2026-04-05T06:07:08.0000000Z", rows[1].DownloadedAt);
    }

    [Fact]
    public async Task InvalidTransactions_AreRejectedAndCounted_ValidOnesStored()
    {
        using var harness = new TrackerHarness();
        Transaction[] batch =
        [
            Tx(externalId: "ok-1"),
            Tx(externalId: ""),
            Tx(institution: ""),
            Tx(institution: "a|b"),
            Tx(payee: ""),
            null!,
            Tx(externalId: "ok-2"),
        ];

        var result = await harness.Tracker.TrackAsync(batch, "1.0.0", Token);

        Assert.Equal(new(2, 0, 5), result);
        Assert.Equal(
            ["ok-1", "ok-2"],
            (await harness.Store.GetTransactionsAsync(Token)).Select(static transaction => transaction.ExternalId).ToArray());
    }

    [Fact]
    public async Task RejectedTransaction_DoesNotStopTheRestOfTheBatch()
    {
        using var harness = new TrackerHarness();

        var result = await harness.Tracker.TrackAsync([Tx(externalId: ""), Tx(externalId: "valid")], "1.0.0", Token);

        Assert.Equal(new(1, 0, 1), result);
        Assert.Equal(1, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task Rejections_LogOneWarningWithIndexAndReason_ButNoValues()
    {
        using var harness = new TrackerHarness();
        var secret = Tx(externalId: "", payee: "SECRET-PAYEE", amount: 987.65m);

        await harness.Tracker.TrackAsync([Tx(externalId: "ok"), secret], "1.0.0", Token);

        var warning = Assert.Single(harness.Logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Contains("index 1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("externalId is empty", warning.Message, StringComparison.Ordinal);
        Assert.All(harness.Logger.Entries, entry =>
        {
            Assert.DoesNotContain("SECRET-PAYEE", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("987.65", entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task EachCall_LogsOneInformationSummary()
    {
        using var harness = new TrackerHarness();
        await harness.Tracker.TrackAsync([Tx(externalId: "1"), Tx(externalId: "1"), Tx(externalId: "")], "1.0.0", Token);

        var summary = Assert.Single(harness.Logger.Entries, static entry => entry.Level == LogLevel.Information);

        Assert.Equal("Tracked 3 transactions: 1 inserted, 1 skipped, 1 rejected.", summary.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BlankPluginVersion_Throws_AndStoresNothing(string? version)
    {
        using var harness = new TrackerHarness();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.Tracker.TrackAsync([Tx()], version!, Token));
        Assert.Equal(0, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task NullTransactions_Throws()
    {
        using var harness = new TrackerHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Tracker.TrackAsync(null!, "1.0.0", Token));
    }

    [Fact]
    public async Task PreCancelledToken_Throws_AndStoresNothing()
    {
        using var harness = new TrackerHarness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Tracker.TrackAsync([Tx()], "1.0.0", cts.Token));
        Assert.Equal(0, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task CancellationMidBatch_Throws_AndKeepsRowsAlreadyInserted()
    {
        using var harness = new TrackerHarness();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);

        IEnumerable<Transaction> Batch()
        {
            yield return Tx(externalId: "1");
            yield return Tx(externalId: "2");
            cts.Cancel();
            yield return Tx(externalId: "3");
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Tracker.TrackAsync(Batch(), "1.0.0", cts.Token));
        Assert.Equal(2, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task Sequence_IsEnumeratedOnce()
    {
        using var harness = new TrackerHarness();
        var sequence = new CountingSequence([Tx(externalId: "1"), Tx(externalId: "2")]);

        await harness.Tracker.TrackAsync(sequence, "1.0.0", Token);

        Assert.Equal(1, sequence.EnumerationCount);
    }

    [Fact]
    public async Task Tracker_NeverChecksForTheHashBeforeInserting()
    {
        using var harness = new TrackerHarness();
        var store = new HasCountingStore(harness.Store);

        await harness.NewTracker(store).TrackAsync([Tx(externalId: "1"), Tx(externalId: "1"), Tx(externalId: "2")], "1.0.0", Token);

        Assert.Equal(0, store.HasCalls);
    }

    [Fact]
    public async Task StoreFailure_Propagates_AndLeavesEarlierRowsInserted()
    {
        using var harness = new TrackerHarness();
        var tracker = harness.NewTracker(new FailingStore(harness.Store, failOnRecordCall: 2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tracker.TrackAsync([Tx(externalId: "1"), Tx(externalId: "2"), Tx(externalId: "3")], "1.0.0", Token));

        Assert.Equal(1, await harness.CountRowsAsync(Token));
    }

    private sealed class CountingSequence(IReadOnlyList<Transaction> items) : IEnumerable<Transaction>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<Transaction> GetEnumerator()
        {
            EnumerationCount++;
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
