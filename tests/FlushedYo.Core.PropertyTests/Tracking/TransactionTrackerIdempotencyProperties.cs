using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FlushedYo.Core.Models;
using FlushedYo.Core.Tests.Tracking;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.PropertyTests.Tracking;

/// <summary>
/// Property family for tracker idempotency over a real SQLite ledger (spec: transaction_tracker_dedup).
/// Generated batches duplicate identities as cosmetic or exact variants within and across batches;
/// the atomic <c>INSERT OR IGNORE</c> path must insert each identity exactly once and keep the first
/// variant's original text and <see cref="Transaction.Type"/>. Concurrency and failure-injection
/// scenarios stay as example tests in <c>TransactionTrackerDedupTests</c> and
/// <c>SqliteTransactionStoreDedupTests</c> (spec: property-based testing introduction).
/// </summary>
public sealed class TransactionTrackerIdempotencyProperties
{
    private const string PluginVersion = "1.0.0";

    // Idempotency over generated batch lists: duplicates inside and across batches, cosmetic
    // variants and brand-new identities, tracked sequentially through ONE tracker.
    [Property(MaxTest = 25)]
    public Property TrackingGeneratedBatches_IsFullyIdempotent()
    {
        var token = TestContext.Current.CancellationToken;
        return Prop.ForAll(BatchScenario().ToArbitrary(), async batches =>
        {
            using var harness = new TrackerHarness();
            var results = new List<TrackingResult>();

            foreach (var batch in batches)
            {
                results.Add(await harness.Tracker.TrackAsync(batch, PluginVersion, token));
            }

            // Every run examined every entry exactly once: Inserted + Skipped + Rejected == batch size.
            if (!results.Zip(batches, static (result, batch) => result.Total == batch.Length).All(static ok => ok))
            {
                return false;
            }

            // Re-tracking any previously tracked batch inserts nothing.
            foreach (var batch in batches)
            {
                var rerun = await harness.Tracker.TrackAsync(batch, PluginVersion, token);
                if (rerun.Inserted != 0 || rerun.Skipped != batch.Length || rerun.Rejected != 0)
                {
                    return false;
                }
            }

            // The first occurrence of each canonical identity, in track order.
            var firstOccurrences = batches
                .SelectMany(static batch => batch)
                .DistinctBy(CanonicalTransactionHasher.ComputeHash)
                .ToList();

            if (results.Sum(static result => result.Inserted) != firstOccurrences.Count)
            {
                return false;
            }

            if (await harness.CountRowsAsync(token) != firstOccurrences.Count)
            {
                return false;
            }

            // Rows are returned in insertion order, so each stored row must equal the first
            // inserted variant's original text and Type (record equality compares every field).
            var stored = (await harness.Store.GetTransactionsAsync(token)).ToList();
            return stored.Count == firstOccurrences.Count
                && stored.Zip(firstOccurrences, static (actual, expected) => actual == expected).All(static same => same);
        });
    }

    // Order independence: permuting a batch of distinct transactions yields the same rows and counts.
    [Property(MaxTest = 25)]
    public Property PermutingADistinctBatch_YieldsTheSameLedger()
    {
        var token = TestContext.Current.CancellationToken;
        return Prop.ForAll(DistinctPool().ToArbitrary(), async batch =>
        {
            using var original = new TrackerHarness();
            using var permuted = new TrackerHarness();

            // A rotation is a different permutation whenever the batch has more than one identity.
            var shuffled = batch.Length > 1 ? [.. batch[1..], batch[0]] : batch;

            var first = await original.Tracker.TrackAsync(batch, PluginVersion, token);
            var second = await permuted.Tracker.TrackAsync(shuffled, PluginVersion, token);

            if (first.Inserted != batch.Length || first.Skipped != 0 || first.Rejected != 0
                || second.Inserted != batch.Length || second.Skipped != 0 || second.Rejected != 0)
            {
                return false;
            }

            // Row order follows insertion order, so compare the sets by canonical hash.
            var originalRows = (await original.Store.GetTransactionsAsync(token)).ToList();
            var permutedRows = (await permuted.Store.GetTransactionsAsync(token)).ToList();

            return originalRows.Count == permutedRows.Count
                && originalRows.Select(CanonicalTransactionHasher.ComputeHash).Order().SequenceEqual(
                    permutedRows.Select(CanonicalTransactionHasher.ComputeHash).Order());
        });
    }

    /// <summary>
    /// 1..3 batches, each of 1..10 entries. Every entry is a valid transaction: a cosmetic variant
    /// of a pool identity (3 in 5), an exact pool copy (1 in 5), or a brand-new identity (1 in 5),
    /// so duplicates arise both within and across batches.
    /// </summary>
    private static Gen<Transaction[][]> BatchScenario() =>
        from pool in DistinctPool()
        from batchCount in Gen.Choose(1, 3)
        from batches in Gen.ArrayOf(BatchOf(pool), batchCount)
        select batches;

    /// <summary>
    /// 1..4 transactions with forced-distinct external ids (<c>id-0</c>..<c>id-N</c>), so the pool
    /// holds pairwise-distinct normalized identities no matter what the other fields generate.
    /// </summary>
    private static Gen<Transaction[]> DistinctPool() =>
        from count in Gen.Choose(1, 4)
        from transactions in Gen.ArrayOf(TrackingGenerators.Canonical(), count)
        let pool = transactions.Select((transaction, index) => transaction with { ExternalId = "id-" + index }).ToArray()
        select pool;

    /// <summary>A batch of 1..10 generated entries.</summary>
    private static Gen<Transaction[]> BatchOf(Transaction[] pool) =>
        from size in Gen.Choose(1, 10)
        from entries in Gen.ArrayOf(Entry(pool), size)
        select entries;

    /// <summary>
    /// A single batch entry: three-in-five a cosmetic variant of a pool identity, one-in-five an
    /// exact pool copy, one-in-five a brand-new identity.
    /// </summary>
    private static Gen<Transaction> Entry(Transaction[] pool) =>
        Gen.Frequency(
            (3, VariantOfPoolMember(pool)),
            (1, Gen.Elements(pool)),
            (1, NewIdentity()));

    /// <summary>A cosmetic variant of a randomly chosen pool identity.</summary>
    private static Gen<Transaction> VariantOfPoolMember(Transaction[] pool) =>
        from member in Gen.Elements(pool)
        from variant in TrackingGenerators.VariantOf(member)
        select variant;

    /// <summary>
    /// A brand-new identity outside the pool's <c>id-N</c> namespace. Two draws may coincide, which
    /// is fine: a coinciding draw is just another duplicate the tracker must skip.
    /// </summary>
    private static Gen<Transaction> NewIdentity() =>
        from seed in Gen.Choose(0, 1_000_000)
        from transaction in TrackingGenerators.Canonical()
        select transaction with { ExternalId = "new-" + seed };
}
