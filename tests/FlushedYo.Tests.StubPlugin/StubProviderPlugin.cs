using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;

namespace FlushedYo.Tests.StubPlugin;

/// <summary>
/// A data-driven <see cref="IProviderPlugin"/> for tests. It never reads or emits a secret value; with
/// <see cref="StubPluginData.SecretKey"/> set it only asks whether the secret exists.
/// </summary>
public sealed class StubProviderPlugin : IProviderPlugin, IDisposable
{
    private readonly PluginContext _context;
    private readonly StubPluginData _data;

    /// <summary>Creates the plugin and reads <c>stub.json</c> from its folder.</summary>
    /// <param name="context">The services supplied by the loader.</param>
    public StubProviderPlugin(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _data = StubPluginData.Load(context.Descriptor.DirectoryPath);
    }

    /// <inheritdoc />
    public string Name => _context.Descriptor.Name;

    /// <inheritdoc />
    public string Version => _data.ReportVersion ?? _context.Descriptor.Version;

    /// <inheritdoc />
    public async Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_data.Throw is not null)
        {
            throw new InvalidOperationException(_data.Throw);
        }

        var transactions = _data.Transactions
            .Select(entry => new Transaction
            {
                Institution = entry.Institution ?? Name,
                ExternalId = entry.ExternalId,
                Amount = entry.Amount,
                Date = entry.Date,
                Payee = entry.Payee,
                Type = entry.Type,
            })
            .ToList();

        if (_data.SecretKey is not null)
        {
            var exists = await _context.SecretStore.SecretExistsAsync(_data.SecretKey, cancellationToken).ConfigureAwait(false);
            transactions.Add(new Transaction
            {
                Institution = Name,
                ExternalId = "secret-check",
                Amount = 0.01m,
                Date = new DateOnly(2026, 1, 1),
                Payee = exists ? "secret:present" : "secret:absent",
                Type = "check",
            });
        }

        return transactions;
    }

    /// <inheritdoc />
    public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Holding>>([]);

    /// <summary>Writes the dispose marker file, when one is configured.</summary>
    public void Dispose()
    {
        if (_data.DisposeMarker is not null)
        {
            File.WriteAllText(Path.Combine(_context.Descriptor.DirectoryPath, _data.DisposeMarker), "disposed");
        }
    }
}
