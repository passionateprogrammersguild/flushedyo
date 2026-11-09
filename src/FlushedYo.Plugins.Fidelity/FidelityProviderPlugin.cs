using FlushedYo.Core.AgentBridge;
using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// The Fidelity provider plugin: downloads Fidelity transactions through the trusted
/// browser bridge, maps them to canonical transactions, and advances account-scoped
/// masked watermarks only after a successful import.
/// </summary>
/// <remarks>
/// The account-scoped import and its inclusive safe-date watermark are persisted atomically by
/// <see cref="AccountTransactionImporter"/>; this plugin never writes a watermark itself and never
/// touches an artifact before its import has completed. Log messages carry counts and value-free
/// reasons only — never a payee, amount, account number, external ID, hash, or secret.
/// </remarks>
public sealed partial class FidelityProviderPlugin : IProviderPlugin, IAsyncDisposable
{
    /// <summary>The institution key reported by this plugin.</summary>
    public const string PluginName = "fidelity";

    /// <summary>The plugin version, matching <c>plugins/fidelity/plugin.json</c>.</summary>
    public const string PluginVersion = "1.0.0";

    private readonly IFidelityStatementSource _statementSource;
    private readonly FidelityProviderOptions _options;
    private readonly AccountTransactionImporter _importer;
    private readonly FidelityCsvAdapter _adapter;
    private readonly ILogger<FidelityProviderPlugin> _logger;

    /// <summary>Initializes the plugin.</summary>
    public FidelityProviderPlugin(
        IFidelityStatementSource statementSource,
        FidelityProviderOptions options,
        AccountTransactionImporter importer,
        ILogger<FidelityProviderPlugin> logger)
    {
        ArgumentNullException.ThrowIfNull(statementSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(logger);
        _statementSource = statementSource;
        _options = options;
        _importer = importer;
        _adapter = new FidelityCsvAdapter(InstitutionIdentity.Fidelity);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PluginName;

    /// <inheritdoc />
    public string Version => PluginVersion;

    /// <inheritdoc />
    /// <remarks>
    /// Legacy <see cref="IProviderPlugin"/> callers get every mapped transaction across accounts, but
    /// they lose the account-scoped dedup and watermark guarantee. Prefer <see cref="DownloadAccountsAsync"/>.
    /// </remarks>
    public async Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await DownloadAccountsAsync(cancellationToken).ConfigureAwait(false);
        return result.Transactions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fidelity holdings are deliberately unsupported: the typed download capability of unit 011
    /// delivers transaction exports only, and no positions CSV contract has been verified. The plugin
    /// therefore declares only <c>download-transactions</c> and returns an empty set here rather than
    /// inventing a positions format.
    /// </remarks>
    public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default)
    {
        LogHoldingsUnsupported(_logger);
        return Task.FromResult<IEnumerable<Holding>>([]);
    }

    /// <summary>Disposes the statement source, and with it the trusted browser session.</summary>
    public ValueTask DisposeAsync() => _statementSource is IAsyncDisposable disposable
        ? disposable.DisposeAsync()
        : ValueTask.CompletedTask;
}
