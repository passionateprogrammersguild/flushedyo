using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Tracking;

/// <summary>
/// Validates and materializes one account download, then persists all valid rows and their import/cash
/// progress atomically. Invalid records are counted without logging financial values.
/// </summary>
public sealed partial class AccountTransactionImporter(
    IAccountLedgerStore store,
    TimeProvider timeProvider,
    ILogger<AccountTransactionImporter> logger)
{
    /// <summary>Imports one account batch using a single deterministic timestamp.</summary>
    /// <param name="account">Account that owns every transaction in the batch.</param>
    /// <param name="transactions">Downloaded transactions, enumerated exactly once.</param>
    /// <param name="pluginVersion">Version of the plugin that supplied the batch.</param>
    /// <param name="openingCashBalance">
    /// Optional opening cash baseline. When omitted, a previously known cash balance is used; the first
    /// import without either baseline remains explicitly unknown.
    /// </param>
    /// <param name="cancellationToken">Token that cancels enumeration or persistence.</param>
    /// <returns>The atomically persisted import result.</returns>
    /// <remarks>
    /// Validation rejections do not advance the safe-date watermark and make cash reconciliation necessary.
    /// Store failures roll back the entire valid batch, so retrying cannot double-apply cash movements.
    /// </remarks>
    public async Task<AccountImportResult> ImportAsync(
        Account account,
        IEnumerable<AccountTransaction> transactions,
        string pluginVersion,
        decimal? openingCashBalance = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
        if (!AccountTransactionHasher.TryValidateAccount(account, out var accountError))
        {
            throw new ArgumentException(accountError, nameof(account));
        }

        var valid = new List<AccountTransaction>();
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

            if (!AccountTransactionHasher.TryValidate(transaction, out var error))
            {
                rejected++;
                LogRejected(logger, index, error!);
                continue;
            }

            var normalizedAccount = AccountTransactionHasher.NormalizeAccount(account);
            var itemAccount = AccountTransactionHasher.NormalizeAccount(transaction.Account);
            if (!string.Equals(normalizedAccount.Institution, itemAccount.Institution, StringComparison.Ordinal) ||
                !string.Equals(normalizedAccount.AccountKey, itemAccount.AccountKey, StringComparison.Ordinal))
            {
                rejected++;
                LogRejected(logger, index, "transaction belongs to a different account");
                continue;
            }

            valid.Add(transaction);
        }

        var result = await store.ImportAsync(new AccountImport
        {
            Account = account,
            Transactions = valid,
            PluginVersion = pluginVersion,
            ImportedAt = timeProvider.GetUtcNow(),
            OpeningCashBalance = openingCashBalance,
            Rejected = rejected,
        }, cancellationToken).ConfigureAwait(false);

        LogSummary(logger, result.Total, result.Inserted, result.Skipped, result.Rejected);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected account transaction at index {Index}: {Reason}.")]
    private static partial void LogRejected(ILogger logger, int index, string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Imported {Total} account transactions: {Inserted} inserted, {Skipped} skipped, {Rejected} rejected.")]
    private static partial void LogSummary(ILogger logger, int total, int inserted, int skipped, int rejected);
}
