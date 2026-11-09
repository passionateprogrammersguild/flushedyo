using FlushedYo.Core.Models;
using FlushedYo.Plugins.Fidelity.Tests.Support;

namespace FlushedYo.Plugins.Fidelity.Tests;

public sealed class FidelityProviderPluginTests
{
    [Fact]
    public async Task ImportsEachAccountAndAdvancesWatermarksOnlyAfterImport()
    {
        await using var harness = new FidelityHarness();
        var firstKey = FidelityCsvFixture.AccountKey("X100");
        var secondKey = FidelityCsvFixture.AccountKey("X200");
        var source = harness.Source(new Dictionary<string, string>
        {
            [firstKey] = FidelityCsvFixture.Export(
                FidelityCsvFixture.Row("X100", "2026-11-01", "-10.00"),
                FidelityCsvFixture.Row("X100", "2026-11-03", "-20.00")),
            [secondKey] = FidelityCsvFixture.Export(
                FidelityCsvFixture.Row("X200", "2026-11-02", "+5.00")),
        });

        await using var plugin = harness.CreatePlugin(source, FidelityHarness.ProviderOptions(firstKey, secondKey));
        var ct = TestContext.Current.CancellationToken;
        var result = await plugin.DownloadAccountsAsync(ct);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(["open", "login", "list", "download", "download"], source.Calls);
        Assert.Equal(3, result.Transactions.Count);

        Assert.Equal(
            new DateOnly(2026, 11, 3),
            await harness.Store.GetLatestSafeDateAsync(Account(firstKey), ct));
        Assert.Equal(
            new DateOnly(2026, 11, 2),
            await harness.Store.GetLatestSafeDateAsync(Account(secondKey), ct));
    }

    [Fact]
    public async Task RerunIsIdempotentOnSafeDateOverlap()
    {
        await using var harness = new FidelityHarness();
        var key = FidelityCsvFixture.AccountKey("X100");
        var export = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "-10.00"),
            FidelityCsvFixture.Row("X100", "2026-11-02", "-20.00"));
        var options = FidelityHarness.ProviderOptions(key);
        var ct = TestContext.Current.CancellationToken;

        await using (var first = harness.CreatePlugin(harness.Source(new() { [key] = export }), options))
        {
            await first.DownloadAccountsAsync(ct);
        }

        await using var second = harness.CreatePlugin(harness.Source(new() { [key] = export }), options);
        var result = await second.DownloadAccountsAsync(ct);

        var account = Assert.Single(result.Accounts);
        Assert.Equal(2, account.Import!.Skipped);
        Assert.Equal(0, account.Import.Inserted);
        Assert.Equal(new DateOnly(2026, 11, 2), account.Import.LatestSafeDate);
    }

    [Fact]
    public async Task RefusesAnAmbiguousArtifactAndLeavesItsWatermarkUnset()
    {
        await using var harness = new FidelityHarness();
        var key = FidelityCsvFixture.AccountKey("X100");
        var source = harness.Source(new Dictionary<string, string>
        {
            [key] = FidelityCsvFixture.Export(
                FidelityCsvFixture.Row("X100", "2026-11-01", "-10.00"),
                FidelityCsvFixture.Row("X100", "2026-11-02", "10.00")),
        });

        await using var plugin = harness.CreatePlugin(source, FidelityHarness.ProviderOptions(key));
        var ct = TestContext.Current.CancellationToken;
        var result = await plugin.DownloadAccountsAsync(ct);

        var account = Assert.Single(result.Accounts);
        Assert.False(account.Succeeded);
        Assert.Null(account.Import);
        Assert.Null(await harness.Store.GetLatestSafeDateAsync(Account(key), ct));
        Assert.Contains(harness.Logger.Messages(), message => message.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HoldingsAreDeliberatelyUnsupported()
    {
        await using var harness = new FidelityHarness();
        await using var plugin = harness.CreatePlugin(harness.Source([]));

        var holdings = await plugin.DownloadHoldingsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(holdings);
        Assert.Contains(
            harness.Logger.Messages(),
            message => message.Contains("holdings are unsupported", StringComparison.Ordinal));
    }

    private static Account Account(string key) => new() { Institution = "fidelity", AccountKey = key };
}
