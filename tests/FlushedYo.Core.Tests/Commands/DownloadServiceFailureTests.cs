using FlushedYo.Core.Commands;
using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tests.Tracking;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using static FlushedYo.Core.Tests.Commands.DownloadTestData;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Failure isolation, value-free errors, and cancellation (spec: download_service_and_command).</summary>
public sealed class DownloadServiceFailureTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PluginException_IsReportedByTypeNameOnly_NeverByItsMessage()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Throwing("fidelity", new InvalidOperationException("SECRET-IN-MESSAGE")));

        var report = await harness.Service.RunAsync([Runnable("fidelity")], Token);

        var result = Assert.Single(report.Plugins);
        Assert.Equal(new PluginDownloadResult("fidelity", DownloadStatus.Failed, 0, 0, 0, "Plugin threw InvalidOperationException."), result);

        var run = (await harness.History.GetLastRunsAsync(Token))["fidelity"];
        Assert.Equal(DownloadOutcome.Failed, run.Outcome);
        Assert.Equal("Plugin threw InvalidOperationException.", run.Error);
        Assert.Equal((0, 0, 0), (run.Inserted, run.Skipped, run.Rejected));

        Assert.DoesNotContain("SECRET-IN-MESSAGE", CliJson.Serialize(report), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-IN-MESSAGE", run.Error, StringComparison.Ordinal);
        Assert.All(harness.Logger.Entries, entry => Assert.DoesNotContain("SECRET-IN-MESSAGE", entry.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PluginLoadException_IsReportedWithItsMessage_AndRecordedWithTheManifestVersion()
    {
        using var harness = new DownloadHarness();
        harness.Loader.AddFailure("fidelity", new PluginLoadException("Plugin 'fidelity' declares no executable assembly."));

        var report = await harness.Service.RunAsync([Runnable("fidelity", version: "1.2.3")], Token);

        var result = Assert.Single(report.Plugins);
        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Equal("Plugin 'fidelity' declares no executable assembly.", result.Detail);
        var run = (await harness.History.GetLastRunsAsync(Token))["fidelity"];
        Assert.Equal("1.2.3", run.PluginVersion);
        Assert.Equal(result.Detail, run.Error);
    }

    [Fact]
    public async Task SecretStoreException_FromThePlugin_IsReportedWithItsMessage()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Throwing("fidelity", new SecretStoreException("vault is locked")));

        var result = Assert.Single((await harness.Service.RunAsync([Runnable("fidelity")], Token)).Plugins);

        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Equal("vault is locked", result.Detail);
    }

    [Fact]
    public async Task IteratorThatThrowsMidEnumeration_IsAPluginFailure_AndStoresNothing()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(new FakePlugin("fidelity", _ => Task.FromResult(ThrowsAfterOne())));

        var result = Assert.Single((await harness.Service.RunAsync([Runnable("fidelity")], Token)).Plugins);

        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Equal("Plugin threw InvalidOperationException.", result.Detail);
        Assert.Equal(0, await harness.CountRowsAsync(Token));
    }

    [Fact]
    public async Task AFailingPlugin_DoesNotStopTheOthers()
    {
        using var harness = new DownloadHarness();
        harness.Loader
            .Add(FakePlugin.Throwing("alpha", new InvalidOperationException("x")))
            .Add(FakePlugin.Returning("bravo", [Tx(institution: "bravo", externalId: "1"), Tx(institution: "bravo", externalId: "2")]));

        var report = await harness.Service.RunAsync([Runnable("alpha"), Runnable("bravo")], Token);

        Assert.Equal([DownloadStatus.Failed, DownloadStatus.Succeeded], report.Plugins.Select(static result => result.Status).ToArray());
        Assert.Equal(2, report.Plugins[1].Inserted);
        Assert.Equal(2, await harness.CountRowsAsync(Token));
        Assert.Equal(2, await harness.CountRunsAsync(Token));
    }

    [Fact]
    public async Task LongErrors_AreTruncatedToFiveHundredCharacters()
    {
        using var harness = new DownloadHarness();
        harness.Loader.AddFailure("fidelity", new PluginLoadException(new string('x', 600)));

        var result = Assert.Single((await harness.Service.RunAsync([Runnable("fidelity")], Token)).Plugins);

        Assert.Equal(500, result.Detail!.Length);
        Assert.Equal(500, (await harness.History.GetLastRunsAsync(Token))["fidelity"].Error!.Length);
    }

    [Fact]
    public async Task OperationCanceledException_NotCausedByTheCommandToken_IsAPluginFailure()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Throwing("fidelity", new OperationCanceledException()));

        var result = Assert.Single((await harness.Service.RunAsync([Runnable("fidelity")], Token)).Plugins);

        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Equal("Plugin threw OperationCanceledException.", result.Detail);
    }

    [Fact]
    public async Task CancelledBeforeRunning_Throws_WithoutLoadingOrRecordingAnything()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx()]));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Service.RunAsync([Runnable("fidelity")], cts.Token));

        Assert.Equal(0, harness.Loader.LoadCalls);
        Assert.False(harness.Ledger.Database.Exists);
    }

    [Fact]
    public async Task CancelledDuringAPluginsDownload_Propagates_AndLeavesNoRowForThatPlugin()
    {
        using var harness = new DownloadHarness();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        harness.Loader
            .Add(FakePlugin.Returning("alpha", [Tx(institution: "alpha")]))
            .Add(new FakePlugin("bravo", async _ =>
            {
                await cts.CancelAsync();
                throw new OperationCanceledException(cts.Token);
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Service.RunAsync([Runnable("alpha"), Runnable("bravo")], cts.Token));

        var runs = await harness.History.GetLastRunsAsync(Token);
        Assert.Equal(["alpha"], runs.Keys.ToArray());
    }

    [Fact]
    public async Task LedgerFailure_IsACommandFailure_NotAPluginFailure()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx()]));
        var service = harness.NewService(history: new LedgerBrokenHistory());

        await Assert.ThrowsAsync<LedgerException>(() => service.RunAsync([Runnable("fidelity")], Token));
    }

    [Fact]
    public async Task TrackerFailure_Propagates_AndRecordsNoRunForThatPlugin()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx(externalId: "1"), Tx(externalId: "2")]));
        var failingTracker = new TransactionTracker(new FailingStore(harness.Store, failOnRecordCall: 1), harness.Time, NullLogger<TransactionTracker>.Instance);
        var service = harness.NewService(tracker: failingTracker);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync([Runnable("fidelity")], Token));

        Assert.Equal(0, await harness.CountRunsAsync(Token));
    }

    [Fact]
    public async Task LoadedPlugin_IsDisposedEvenWhenItsRunFails()
    {
        using var harness = new DownloadHarness();
        var plugin = FakePlugin.Throwing("fidelity", new InvalidOperationException("x"));
        harness.Loader.Add(plugin);

        await harness.Service.RunAsync([Runnable("fidelity")], Token);

        Assert.Equal(1, plugin.DisposeCount);
    }

    private static IEnumerable<Transaction> ThrowsAfterOne()
    {
        yield return Tx(externalId: "1");
        throw new InvalidOperationException("boom");
    }

    private sealed class LedgerBrokenHistory : IRunHistoryStore
    {
        public Task RecordRunAsync(DownloadRun run, CancellationToken cancellationToken = default) =>
            throw new LedgerException("ledger broken");

        public Task<IReadOnlyDictionary<string, DownloadRun>> GetLastRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DownloadRun>>(new Dictionary<string, DownloadRun>());

        public Task<IReadOnlyDictionary<string, long>> GetStoredTransactionCountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());
    }
}
