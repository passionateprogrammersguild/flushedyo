using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Tracking;

/// <summary>
/// Records a batch of downloaded transactions idempotently: validate, compute the canonical hash,
/// then insert atomically. Re-tracking the same data never creates a second row (Concern C).
/// </summary>
/// <remarks>
/// The tracker relies on the store's atomic insert result rather than checking for the hash first,
/// which would be racy. It never logs payee, amount, external ID, institution, or hash values.
/// </remarks>
public sealed partial class TransactionTracker(
    ITransactionStore store,
    TimeProvider timeProvider,
    ILogger<TransactionTracker> logger)
{
    /// <summary>Tracks <paramref name="transactions"/> and reports what happened to each.</summary>
    /// <param name="transactions">The transactions to record. Enumerated exactly once.</param>
    /// <param name="pluginVersion">Version of the plugin that downloaded them, recorded for auditability.</param>
    /// <param name="cancellationToken">Token that cancels the batch between transactions.</param>
    /// <returns>The inserted, skipped, and rejected counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transactions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pluginVersion"/> is null or whitespace.</exception>
    /// <remarks>
    /// Store failures propagate and abort the batch. Rows already inserted stay, and re-running is safe
    /// because inserts are idempotent. All transactions in a batch share one download timestamp.
    /// </remarks>
    public async Task<TrackingResult> TrackAsync(
        IEnumerable<Transaction> transactions,
        string pluginVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);

        var downloadedAt = timeProvider.GetUtcNow();
        var inserted = 0;
        var skipped = 0;
        var rejected = 0;
        var index = -1;

        foreach (var transaction in transactions)
        {
            index++;
            cancellationToken.ThrowIfCancellationRequested();

            if (transaction is null)
            {
                rejected++;
                LogRejected(logger, index, "transaction is null");
                continue;
            }

            if (!CanonicalTransactionHasher.TryValidate(transaction, out var error))
            {
                rejected++;
                LogRejected(logger, index, error!);
                continue;
            }

            var audit = new TransactionAudit(CanonicalTransactionHasher.ComputeHash(transaction), pluginVersion, downloadedAt);
            if (await store.RecordTransactionAsync(transaction, audit, cancellationToken).ConfigureAwait(false))
            {
                inserted++;
            }
            else
            {
                skipped++;
            }
        }

        var result = new TrackingResult(inserted, skipped, rejected);
        LogSummary(logger, result.Total, result.Inserted, result.Skipped, result.Rejected);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected transaction at index {Index}: {Reason}.")]
    private static partial void LogRejected(ILogger logger, int index, string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Tracked {Total} transactions: {Inserted} inserted, {Skipped} skipped, {Rejected} rejected.")]
    private static partial void LogSummary(ILogger logger, int total, int inserted, int skipped, int rejected);
}
