using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// The per-account leg of the Fidelity workflow: download one artifact, adapt it, and import the mapped
/// rows through <see cref="AccountTransactionImporter"/>, which advances the account's inclusive safe-date
/// watermark atomically with the import. Nothing is imported — and no watermark moves — unless the whole
/// artifact maps cleanly.
/// </summary>
public sealed partial class FidelityProviderPlugin
{
    private async Task<FidelityAccountDownload> DownloadAccountAsync(
        string accountKey,
        CancellationToken cancellationToken)
    {
        var account = new Account { Institution = PluginName, AccountKey = accountKey };
        CsvAdapterResult adapted;
        try
        {
            var artifact = await _statementSource
                .DownloadStatementAsync(accountKey, cancellationToken)
                .ConfigureAwait(false);
            var csv = FidelityArtifactReader.Read(artifact.Path, _options.MaxArtifactBytes);
            adapted = _adapter.Adapt(csv, accountKey);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FidelityAdapterException or FlushedYo.Core.AgentBridge.BridgeException)
        {
            return Failed(account, ex.Message);
        }

        if (adapted.Ambiguous.Count > 0)
        {
            // A partial import would advance the inclusive watermark past rows it never stored, so an
            // ambiguous artifact fails the whole account closed and is retried by the coordinator.
            LogAmbiguousArtifact(_logger, adapted.Ambiguous.Count);
            return Failed(account, "The statement export contained rows that could not be read unambiguously.");
        }

        try
        {
            var import = await _importer.ImportAsync(
                account,
                adapted.Transactions.Select(transaction => new AccountTransaction
                {
                    Account = account,
                    Transaction = transaction,
                    CashImpact = CashImpact.Unknown,
                }),
                PluginVersion,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogAccountImported(_logger, import.Inserted, import.Skipped, import.Rejected);
            return new FidelityAccountDownload
            {
                Account = account,
                Mapped = adapted.Transactions.Count,
                Ambiguous = 0,
                Transactions = adapted.Transactions,
                Import = import,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is LedgerException or ArgumentException)
        {
            return Failed(account, ex.Message);
        }
    }

    private static FidelityAccountDownload Failed(Account account, string failure) => new()
    {
        Account = account,
        Mapped = 0,
        Ambiguous = 0,
        Failure = failure,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a Fidelity artifact: {Count} ambiguous rows; nothing was imported.")]
    private static partial void LogAmbiguousArtifact(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Imported a Fidelity account export: {Inserted} inserted, {Skipped} skipped, {Rejected} rejected.")]
    private static partial void LogAccountImported(ILogger logger, int inserted, int skipped, int rejected);
}
