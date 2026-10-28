namespace FlushedYo.Core.Store;

/// <summary>
/// Records download runs and answers the questions <c>status</c> asks of the ledger.
/// </summary>
public interface IRunHistoryStore
{
    /// <summary>Records one download run. Creates and migrates the ledger if it does not exist yet.</summary>
    /// <param name="run">The run to record.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task RecordRunAsync(DownloadRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent run (highest id) for each plugin, keyed by plugin name
    /// (case-insensitive). Returns an empty result without creating the ledger when it does not exist.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>The latest run per plugin.</returns>
    Task<IReadOnlyDictionary<string, DownloadRun>> GetLastRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns how many transactions are stored per institution, keyed by lower-cased institution
    /// (case-insensitive lookup). Returns an empty result without creating the ledger when it does not exist.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>Stored transaction counts per institution.</returns>
    Task<IReadOnlyDictionary<string, long>> GetStoredTransactionCountsAsync(CancellationToken cancellationToken = default);
}
