using Microsoft.Extensions.Logging;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// The account-scoped half of the Fidelity plugin: open → login → per-account download → adapt →
/// import, with one outcome per account. A failing account never stops the others and never advances
/// a watermark, because the watermark is written only by a completed import.
/// </summary>
public sealed partial class FidelityProviderPlugin
{
    private const int MaximumAccountKeyLength = 256;

    /// <summary>
    /// Runs the full multi-account workflow and returns one outcome per configured account.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the run.</param>
    /// <returns>Per-account outcomes plus the flattened mapped transactions.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled the run.</exception>
    /// <exception cref="FidelityAdapterException">The account configuration is invalid, so no browser work started.</exception>
    public async Task<FidelityDownloadResult> DownloadAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accountKeys = ValidateAccountKeys();
        await OpenAndLoginAsync(cancellationToken).ConfigureAwait(false);

        var accounts = new List<FidelityAccountDownload>(accountKeys.Count);
        foreach (var accountKey in accountKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accounts.Add(await DownloadAccountAsync(accountKey, cancellationToken).ConfigureAwait(false));
        }

        return new FidelityDownloadResult
        {
            Accounts = accounts,
            Transactions = [.. accounts.SelectMany(static account => account.Transactions)],
        };
    }

    private async Task OpenAndLoginAsync(CancellationToken cancellationToken)
    {
        await _statementSource.OpenAsync(_options.LoginUrl, cancellationToken).ConfigureAwait(false);
        await _statementSource.LoginAsync(cancellationToken).ConfigureAwait(false);

        // The configured keys are authoritative; the portal cannot be enumerated through the typed
        // bridge, so a login that reports a different set is a contract violation, not a guess target.
        var discovered = await _statementSource.ListAccountsAsync(cancellationToken).ConfigureAwait(false);
        LogSessionReady(_logger, discovered.Count);
    }

    private List<string> ValidateAccountKeys()
    {
        var keys = _options.AccountKeys;
        if (keys is null || keys.Count == 0)
        {
            throw new FidelityAdapterException(
                "No account keys are configured; Fidelity cannot be enumerated without them.");
        }

        var result = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            var trimmed = key?.Trim();
            if (trimmed is null || !IsValidAccountKey(trimmed))
            {
                throw new FidelityAdapterException("A configured account key is empty, too long, or not log-safe.");
            }

            if (!result.Contains(trimmed, StringComparer.Ordinal))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    /// <summary>Validates one trimmed account key in isolation.</summary>
    private static bool IsValidAccountKey(string key)
    {
        if (key.Length is 0 or > MaximumAccountKeyLength)
        {
            return false;
        }

        foreach (var character in key)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fidelity session ready; the portal reported {Count} account keys.")]
    private static partial void LogSessionReady(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fidelity holdings are unsupported by this plugin; returning no holdings.")]
    private static partial void LogHoldingsUnsupported(ILogger logger);
}
