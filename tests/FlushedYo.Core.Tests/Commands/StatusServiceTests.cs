using FlushedYo.Core.Commands;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tests.Store;
using static FlushedYo.Core.Tests.Commands.CommandTestData;
using static FlushedYo.Core.Tests.Store.RunTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Building the status report (spec: status_service_and_command).</summary>
public sealed class StatusServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InstalledPlugin_ShowsItsLastRunAndStoredCount()
    {
        var run = Run(plugin: "fidelity");
        var service = new StatusService(new FakeRunHistory([run], new Dictionary<string, long> { ["fidelity"] = 7 }));

        var report = await service.GetAsync([Descriptor("fidelity")], includeUninstalled: false, Token);

        var status = Assert.Single(report.Plugins);
        Assert.Equal(new PluginStatus("fidelity", "1.0.0", true, run, 7), status);
    }

    [Fact]
    public async Task InstalledPlugin_ThatNeverRan_HasNoRunAndZeroStored()
    {
        var report = await new StatusService(new FakeRunHistory()).GetAsync([Descriptor("fidelity")], false, Token);

        var status = Assert.Single(report.Plugins);
        Assert.Null(status.LastRun);
        Assert.Equal(0, status.StoredTransactions);
        Assert.True(status.Installed);
    }

    [Fact]
    public async Task InstalledPlugins_KeepTheGivenOrder()
    {
        var report = await new StatusService(new FakeRunHistory()).GetAsync([Descriptor("zeta"), Descriptor("alpha")], false, Token);

        Assert.Equal(["zeta", "alpha"], report.Plugins.Select(static status => status.Name).ToArray());
    }

    [Fact]
    public async Task Matching_IsCaseInsensitive_EvenWhenTheStoreKeysAreNot()
    {
        var run = Run(plugin: "Fidelity");
        var service = new StatusService(new FakeRunHistory([run], new Dictionary<string, long> { ["FIDELITY"] = 3 }));

        var report = await service.GetAsync([Descriptor("fidelity")], includeUninstalled: false, Token);

        var status = Assert.Single(report.Plugins);
        Assert.Equal(run, status.LastRun);
        Assert.Equal(3, status.StoredTransactions);
    }

    [Fact]
    public async Task IncludeUninstalled_AppendsInstitutionsWithRowsOrRuns_Sorted()
    {
        var run = Run(plugin: "Zulu", pluginVersion: "4.5.6");
        var service = new StatusService(new FakeRunHistory([run], new Dictionary<string, long> { ["alpha"] = 2, ["fidelity"] = 5 }));

        var report = await service.GetAsync([Descriptor("fidelity")], includeUninstalled: true, Token);

        Assert.Equal(["fidelity", "alpha", "Zulu"], report.Plugins.Select(static status => status.Name).ToArray());
        var alpha = report.Plugins[1];
        Assert.False(alpha.Installed);
        Assert.Null(alpha.Version);
        Assert.Null(alpha.LastRun);
        Assert.Equal(2, alpha.StoredTransactions);
        var zulu = report.Plugins[2];
        Assert.False(zulu.Installed);
        Assert.Equal("4.5.6", zulu.Version);
        Assert.Equal(run, zulu.LastRun);
        Assert.Equal(0, zulu.StoredTransactions);
    }

    [Fact]
    public async Task ExcludeUninstalled_OmitsThem()
    {
        var service = new StatusService(new FakeRunHistory([Run(plugin: "gone")], new Dictionary<string, long> { ["gone"] = 4 }));

        var report = await service.GetAsync([Descriptor("fidelity")], includeUninstalled: false, Token);

        Assert.Equal("fidelity", Assert.Single(report.Plugins).Name);
    }

    [Fact]
    public async Task AnInstitutionInBothRunsAndCounts_IsListedOnce_UsingTheRunsOriginalCase()
    {
        var service = new StatusService(new FakeRunHistory([Run(plugin: "Gone")], new Dictionary<string, long> { ["gone"] = 4 }));

        var report = await service.GetAsync([], includeUninstalled: true, Token);

        var status = Assert.Single(report.Plugins);
        Assert.Equal("Gone", status.Name);
        Assert.Equal(4, status.StoredTransactions);
        Assert.NotNull(status.LastRun);
    }

    [Fact]
    public async Task OnAMachineWithNoLedger_EverythingIsNeverRun_AndNothingIsCreated()
    {
        using var ledger = new TempLedger();
        var service = new StatusService(new SqliteRunHistoryStore(ledger.Database));

        var report = await service.GetAsync([Descriptor("fidelity")], includeUninstalled: true, Token);

        var status = Assert.Single(report.Plugins);
        Assert.Null(status.LastRun);
        Assert.Equal(0, status.StoredTransactions);
        Assert.False(Directory.Exists(ledger.DirectoryPath));
        Assert.False(File.Exists(ledger.DatabasePath));
    }

    [Fact]
    public async Task NeverWritesToTheStore()
    {
        // FakeRunHistory.RecordRunAsync throws, so completing proves the service only reads.
        var report = await new StatusService(new FakeRunHistory([Run()])).GetAsync([Descriptor("stub")], true, Token);

        Assert.Single(report.Plugins);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new StatusService(new FakeRunHistory()).GetAsync([Descriptor("fidelity")], false, cts.Token));
    }

    [Fact]
    public async Task NullPlugins_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new StatusService(new FakeRunHistory()).GetAsync(null!, false, Token));
    }
}
