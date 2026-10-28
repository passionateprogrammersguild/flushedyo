using FlushedYo.Core.Models;

namespace FlushedYo.Core.Store;

/// <summary>
/// Persistent ledger of downloaded transactions, keyed by canonical hash so that recording the
/// same transaction twice never creates a duplicate.
/// </summary>
public interface ITransactionStore
{
    /// <summary>Returns true when a transaction with <paramref name="canonicalHash"/> is already stored.</summary>
    /// <param name="canonicalHash">The canonical hash to look up.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns><see langword="true"/> when the hash exists.</returns>
    Task<bool> HasTransactionAsync(string canonicalHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="transaction"/> unless its canonical hash is already stored. The operation
    /// is atomic, so concurrent calls for the same transaction insert exactly one row.
    /// </summary>
    /// <param name="transaction">The transaction to store, with its original (unnormalized) field values.</param>
    /// <param name="audit">The hash, plugin version, and download time to record with it.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a row was inserted; <see langword="false"/> when the hash already
    /// existed. A duplicate is not an error and leaves the existing row untouched.
    /// </returns>
    Task<bool> RecordTransactionAsync(Transaction transaction, TransactionAudit audit, CancellationToken cancellationToken = default);

    /// <summary>Returns every stored transaction in insertion order.</summary>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>The stored transactions, with the field values they were recorded with.</returns>
    Task<IEnumerable<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default);
}
