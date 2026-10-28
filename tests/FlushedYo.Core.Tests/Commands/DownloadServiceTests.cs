using FlushedYo.Core.Commands;
using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.Store;
using Microsoft.Extensions.Logging;
using static FlushedYo.Core.Tests.Commands.DownloadTestData;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Core download behavior (spec: download_service_and_command).</summary>
public sealed class DownloadServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnePlugin_StoresItsTransactions_AndRecordsARunWithThePluginsOwnVersion()
    {
        using var harness = new DownloadHarness();
        Transaction[] transactions = [Tx(externalId: "1"), Tx(externalId: "2"), Tx(externalId: "3")];
        harness.Loader.Add(FakePlugin.Returning("fidelity", transactions, version: "9.9.9"));

        var report = await harness.Service.RunAsync([Runnable("fidelity")], Token);

        Assert.Equal(new PluginDownloadResult("fidelity", DownloadStatus.Succeeded, 3, 0, 0, null), Assert.Single(report.Plugins));
        Assert.Equal(3, await harness.CountRowsAsync(Token));
        var run = (await harness.History.GetLastRunsAsync(Token))["fidelity"];
        Assert.Equal("9.9.9", run.PluginVersion);
        Assert.Equal(DownloadOutcome.Succeeded, run.Outcome);
        Assert.Equal((3, 0, 0), (run.Inserted, run.Skipped, run.Rejected));
        Assert.Null(run.Error);
    }

    [Fact]
    public async Task RunningTwice_StoresEachTransactionOnce_AndRecordsBothRuns()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx(externalId: "1"), Tx(externalId: "2"), Tx(externalId: "3")]));

        await harness.Service.RunAsync([Runnable("fidelity")], Token);
        var second = await harness.Service.RunAsync([Runnable("fidelity")], Token);

        Assert.Equal(new PluginDownloadResult("fidelity", DownloadStatus.Succeeded, 0, 3, 0, null), Assert.Single(second.Plugins));
        Assert.Equal(3, await harness.CountRowsAsync(Token));
        Assert.Equal(2, await harness.CountRunsAsync(Token));
        var last = (await harness.History.GetLastRunsAsync(Token))["fidelity"];
        Assert.Equal((0, 3, 0), (last.Inserted, last.Skipped, last.Rejected));
    }

    [Fact]
    public async Task InstitutionMatching_IsCaseInsensitive()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx(institution: "FIDELITY")]));

        var report = await harness.Service.RunAsync([Runnable("fidelity")], Token);

        Assert.Equal(1, Assert.Single(report.Plugins).Inserted);
    }

    [Fact]
    public async Task MismatchedInstitution_IsRejected_NotStored_AndLoggedByCountOnly()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning(
            "fidelity",
            [Tx(externalId: "1"), Tx(institution: "other-bank", externalId: "2", payee: "SECRET-PAYEE", amount: 987.65m)]));

        var report = await harness.Service.RunAsync([Runnable("fidelity")], Token);

        var result = Assert.Single(report.Plugins);
        Assert.Equal((1, 0, 1), (result.Inserted, result.Skipped, result.Rejected));
        Assert.Equal(1, await harness.CountRowsAsync(Token));
        Assert.Equal(1, (await harness.History.GetLastRunsAsync(Token))["fidelity"].Rejected);

        var warning = Assert.Single(harness.Logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Contains("fidelity", warning.Message, StringComparison.Ordinal);
        Assert.Contains("1", warning.Message, StringComparison.Ordinal);
        Assert.All(harness.Logger.Entries, entry =>
        {
            Assert.DoesNotContain("other-bank", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET-PAYEE", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("987.65", entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task MixedBatch_CountsEachKindOfRejection()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning(
            "fidelity",
            [Tx(externalId: "1"), Tx(externalId: "2"), Tx(institution: "other-bank"), Tx(externalId: "3", payee: ""), null!]));

        var result = Assert.Single((await harness.Service.RunAsync([Runnable("fidelity")], Token)).Plugins);

        Assert.Equal((2, 0, 3), (result.Inserted, result.Skipped, result.Rejected));
    }

    [Fact]
    public async Task Plugins_RunInTheGivenOrder_AndResultsKeepIt()
    {
        using var harness = new DownloadHarness();
        harness.Loader
            .Add(FakePlugin.Returning("schwab", [Tx(institution: "schwab")]))
            .Add(FakePlugin.Returning("fidelity", [Tx(institution: "fidelity")]));

        var report = await harness.Service.RunAsync([Runnable("schwab"), Runnable("fidelity")], Token);

        Assert.Equal(["schwab", "fidelity"], report.Plugins.Select(static result => result.Plugin).ToArray());
        Assert.Equal(["schwab", "fidelity"], harness.Loader.LoadedNames);
    }

    [Fact]
    public async Task RunTimes_ComeFromTheInjectedClock()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(new FakePlugin("fidelity", _ =>
        {
            harness.Time.Now = harness.Time.Now.AddSeconds(5);
            return Task.FromResult<IEnumerable<Transaction>>([Tx()]);
        }));

        await harness.Service.RunAsync([Runnable("fidelity")], Token);

        var run = (await harness.History.GetLastRunsAsync(Token))["fidelity"];
        Assert.Equal(TimeSpan.FromSeconds(5), run.FinishedAt - run.StartedAt);
    }

    [Fact]
    public async Task NotRunnablePlugins_AreReportedWithTheirReason_AndNothingElseHappens()
    {
        using var harness = new DownloadHarness();

        var report = await harness.Service.RunAsync([DescriptorOnly("notes"), WithoutCapability("manual")], Token);

        Assert.Equal(
            [
                new PluginDownloadResult("notes", DownloadStatus.NotRunnable, 0, 0, 0, "declares no executable assembly"),
                new PluginDownloadResult("manual", DownloadStatus.NotRunnable, 0, 0, 0, "does not declare the download-transactions capability"),
            ],
            report.Plugins);
        Assert.Equal(0, harness.Loader.LoadCalls);
        Assert.False(harness.Ledger.Database.Exists);
    }

    [Fact]
    public void GetNotRunnableReason_ReportsAssemblyFirst()
    {
        Assert.Null(DownloadService.GetNotRunnableReason(Runnable("fidelity")));
        Assert.Equal("declares no executable assembly", DownloadService.GetNotRunnableReason(DescriptorOnly("notes")));
        Assert.Equal(
            "does not declare the download-transactions capability",
            DownloadService.GetNotRunnableReason(WithoutCapability("manual")));

        var neither = new PluginDescriptor(new PluginManifest { Name = "x", Version = "1.0.0" }, PluginConfiguration.Default, "/plugins/x");
        Assert.Equal("declares no executable assembly", DownloadService.GetNotRunnableReason(neither));
    }

    [Fact]
    public async Task HoldingsAreNeverRequested()
    {
        // FakePlugin.DownloadHoldingsAsync throws, so a clean run proves it was not called.
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx()]));

        var report = await harness.Service.RunAsync([Runnable("fidelity")], Token);

        Assert.Equal(DownloadStatus.Succeeded, Assert.Single(report.Plugins).Status);
    }

    [Fact]
    public async Task LoadedPlugin_IsDisposedAfterItsRun()
    {
        using var harness = new DownloadHarness();
        var plugin = FakePlugin.Returning("fidelity", [Tx()]);
        harness.Loader.Add(plugin);

        await harness.Service.RunAsync([Runnable("fidelity")], Token);

        Assert.Equal(1, plugin.DisposeCount);
    }

    [Fact]
    public async Task EmptyPluginList_YieldsAnEmptyReport_AndTouchesNothing()
    {
        using var harness = new DownloadHarness();

        var report = await harness.Service.RunAsync([], Token);

        Assert.Empty(report.Plugins);
        Assert.Equal(0, harness.Loader.LoadCalls);
        Assert.False(harness.Ledger.Database.Exists);
    }
}
