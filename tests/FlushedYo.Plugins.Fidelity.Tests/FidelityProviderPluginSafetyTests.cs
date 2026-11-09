using FlushedYo.Core.Models;
using FlushedYo.Plugins.Fidelity.Tests.Support;

namespace FlushedYo.Plugins.Fidelity.Tests;

public sealed class FidelityProviderPluginSafetyTests
{
    [Fact]
    public async Task CancellationStopsTheRunBeforeAnyDownload()
    {
        await using var harness = new FidelityHarness();
        var key = FidelityCsvFixture.AccountKey("X100");
        var source = harness.Source(new Dictionary<string, string>
        {
            [key] = FidelityCsvFixture.Export(FidelityCsvFixture.Row("X100", "2026-11-01", "-1.00")),
        });
        await using var plugin = harness.CreatePlugin(source, FidelityHarness.ProviderOptions(key));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.DownloadAccountsAsync(cancellation.Token));

        Assert.Empty(source.Calls);
        Assert.Null(await harness.Store.GetLatestSafeDateAsync(
            new Account { Institution = "fidelity", AccountKey = key },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadFailureIsIsolatedPerAccount()
    {
        await using var harness = new FidelityHarness();
        var good = FidelityCsvFixture.AccountKey("X100");
        var bad = FidelityCsvFixture.AccountKey("X200");
        var source = new FakeFidelityStatementSource(
            harness.ArtifactDirectory,
            new Dictionary<string, string>
            {
                [good] = FidelityCsvFixture.Export(FidelityCsvFixture.Row("X100", "2026-11-01", "-1.00")),
            },
            new Dictionary<string, Exception>
            {
                [bad] = new FidelityAdapterException("The export trigger produced no artifact."),
            });

        await using var plugin = harness.CreatePlugin(source, FidelityHarness.ProviderOptions(good, bad));
        var result = await plugin.DownloadAccountsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Null(Assert.Single(result.Accounts, account => !account.Succeeded).Import);
    }

    [Fact]
    public async Task LogsNeverContainFinancialValues()
    {
        await using var harness = new FidelityHarness();
        var rawAccount = "Z987654";
        var key = FidelityCsvFixture.AccountKey(rawAccount);
        var source = harness.Source(new Dictionary<string, string>
        {
            [key] = FidelityCsvFixture.Export(
                FidelityCsvFixture.Row(rawAccount, "2026-11-01", "-1234.56", new() { Description = "Sensitive Payee Name" })),
        });

        await using var plugin = harness.CreatePlugin(source, FidelityHarness.ProviderOptions(key));
        await plugin.DownloadAccountsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(harness.Logger.Messages());
        foreach (var message in harness.Logger.Messages())
        {
            Assert.DoesNotContain("Sensitive Payee Name", message, StringComparison.Ordinal);
            Assert.DoesNotContain("1234.56", message, StringComparison.Ordinal);
            Assert.DoesNotContain(rawAccount, message, StringComparison.Ordinal);
            Assert.DoesNotContain(key, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task MissingAccountKeysFailsClosedBeforeBrowserWork()
    {
        await using var harness = new FidelityHarness();
        var source = harness.Source([]);
        await using var plugin = harness.CreatePlugin(source, FidelityHarness.ProviderOptions());

        await Assert.ThrowsAsync<FidelityAdapterException>(
            () => plugin.DownloadAccountsAsync(TestContext.Current.CancellationToken));

        Assert.Empty(source.Calls);
    }
}
