using FlushedYo.Core.Models;

namespace FlushedYo.Core.Store;

/// <summary>
/// Account-scoped transaction persistence, inclusive download boundaries, import history, and cash-only progress.
/// </summary>
/// <remarks>
/// Schema v3 adds a parallel account ledger without changing legacy tables, rows, or hashes. Every legacy
/// row receives an assignment record whose account remains <see langword="NULL"/> during migration because
/// an unknown account is never guessed. Account imports and their cash/progress checkpoints commit in one transaction.
/// </remarks>
public interface IAccountLedgerStore
{
    /// <summary>Atomically records an import, its newly inserted rows, and its account progress.</summary>
    /// <param name="import">The valid batch and optional opening cash baseline.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The persisted counts, newly inserted rows, inclusive safe date, and cash state.</returns>
    /// <exception cref="LedgerException">
    /// A source identity is ambiguous (the same trimmed external ID identifies different transactions,
    /// or a duplicate changes cash classification). The entire import is rolled back rather than losing rows.
    /// </exception>
    Task<AccountImportResult> ImportAsync(AccountImport import, CancellationToken cancellationToken = default);

    /// <summary>Returns one account's completed imports in insertion order.</summary>
    /// <param name="account">Institution-scoped account identity.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>
    /// Only history belonging to <paramref name="account"/>. Each entry contains the counts, inclusive
    /// safe-date watermark, and cash-only state persisted atomically with that import.
    /// </returns>
    Task<IReadOnlyList<AccountImportRecord>> GetImportHistoryAsync(
        Account account,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one account's transactions in insertion order.</summary>
    /// <param name="account">Institution-scoped account identity.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>Only transactions belonging to <paramref name="account"/>.</returns>
    Task<IReadOnlyList<AccountTransaction>> GetTransactionsAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>Reads the current inclusive safe-date watermark for one account.</summary>
    /// <param name="account">Institution-scoped account identity.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>
    /// The latest safe date, or <see langword="null"/> if none exists. Callers must include this date in
    /// the next download (an inclusive overlap), never treat it as an exclusive "after" boundary. Account-scoped
    /// dedup makes same-day overlap safe while allowing late-arriving records.
    /// </returns>
    Task<DateOnly?> GetLatestSafeDateAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>Reads the latest persisted cash-only checkpoint for one account.</summary>
    /// <param name="account">Institution-scoped account identity.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    /// <returns>
    /// The checkpoint, or <see langword="null"/> when the account has never completed an import. Unknown and
    /// reconciliation-needed checkpoints contain no fabricated cash balance or difference.
    /// </returns>
    Task<CashState?> GetCashStateAsync(Account account, CancellationToken cancellationToken = default);
}
