using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Contract shape for <see cref="IProviderPlugin"/> and the canonical models (spec: provider_plugin_contracts).</summary>
public sealed class ProviderPluginContractTests
{
    [Fact]
    public async Task TestDouble_RoundTripsTransactionsAndHoldings()
    {
        var plugin = new FakeProviderPlugin();

        var transactions = await plugin.DownloadTransactionsAsync(TestContext.Current.CancellationToken);
        var holdings = await plugin.DownloadHoldingsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fake", plugin.Name);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.Equal(plugin.Transactions, transactions);
        Assert.Equal(plugin.Holdings, holdings);
    }

    [Fact]
    public async Task DownloadTransactionsAsync_PassesCancellationTokenThrough()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var plugin = new FakeProviderPlugin();

        await plugin.DownloadTransactionsAsync(cts.Token);

        Assert.Equal(cts.Token, plugin.LastToken);
    }

    [Fact]
    public async Task DownloadHoldingsAsync_PassesCancellationTokenThrough()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var plugin = new FakeProviderPlugin();

        await plugin.DownloadHoldingsAsync(cts.Token);

        Assert.Equal(cts.Token, plugin.LastToken);
    }

    [Fact]
    public void Transaction_WithIdenticalValues_IsEqual()
    {
        var transaction = new Transaction
        {
            Institution = "fidelity",
            ExternalId = "abc-123",
            Amount = -12.34m,
            Date = new DateOnly(2026, 1, 2),
            Payee = "Corner Store",
            Type = "debit",
        };

        Assert.Equal(transaction, transaction with { });
        Assert.NotEqual(transaction, transaction with { ExternalId = "abc-124" });
    }

    [Fact]
    public void Holding_WithIdenticalValues_IsEqual()
    {
        var holding = new Holding
        {
            Institution = "fidelity",
            Symbol = "VTI",
            Quantity = 10.5m,
            MarketValue = 2500.75m,
            AsOf = new DateOnly(2026, 1, 2),
        };

        Assert.Equal(holding, holding with { });
        Assert.NotEqual(holding, holding with { Symbol = "VOO" });
    }

    private sealed class FakeProviderPlugin : IProviderPlugin
    {
        public string Name => "fake";

        public string Version => "1.0.0";

        public CancellationToken LastToken { get; private set; }

        public Transaction[] Transactions { get; } =
        [
            new()
            {
                Institution = "fake",
                ExternalId = "1",
                Amount = 5m,
                Date = new DateOnly(2026, 1, 1),
                Payee = "Test",
                Type = "credit",
            },
        ];

        public Holding[] Holdings { get; } =
        [
            new()
            {
                Institution = "fake",
                Symbol = "TEST",
                Quantity = 1m,
                MarketValue = 10m,
                AsOf = new DateOnly(2026, 1, 1),
            },
        ];

        public Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return Task.FromResult<IEnumerable<Transaction>>(Transactions);
        }

        public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return Task.FromResult<IEnumerable<Holding>>(Holdings);
        }
    }
}
