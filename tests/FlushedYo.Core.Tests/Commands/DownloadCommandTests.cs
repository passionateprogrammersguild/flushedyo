using System.Text.Json;
using FlushedYo.Core.Commands;
using FlushedYo.Core.Plugin;
using static FlushedYo.Core.Tests.Commands.DownloadTestData;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Selection, output, and exit codes for download (spec: download_service_and_command).</summary>
public sealed class DownloadCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly CommandLineOptions Table = new(null, false);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        DownloadHarness harness,
        CommandLineOptions options,
        params PluginDescriptor[] plugins)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var command = new DownloadCommand(new StubPluginCatalog(plugins), harness.Service);

        var exitCode = await command.ExecuteAsync(options, output, error, Token);

        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task AllSucceed_ReturnsZero_WithATable()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx()]));

        var (exitCode, output, error) = await RunAsync(harness, Table, Runnable("fidelity"));

        Assert.Equal(0, exitCode);
        Assert.Contains("succeeded", output, StringComparison.Ordinal);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task OneFailure_ReturnsTwo_AndTheTableModeAddsAHint()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Throwing("fidelity", new InvalidOperationException("x")));

        var (exitCode, output, error) = await RunAsync(harness, Table, Runnable("fidelity"));

        Assert.Equal(2, exitCode);
        Assert.Contains("failed", output, StringComparison.Ordinal);
        Assert.Contains("One or more plugins failed.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonMode_KeepsStderrEmpty_EvenWhenAPluginFailed()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Throwing("fidelity", new InvalidOperationException("x")));

        var (exitCode, output, error) = await RunAsync(harness, new CommandLineOptions(null, true), Runnable("fidelity"));

        Assert.Equal(2, exitCode);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task PluginFlag_RunsOnlyThatPlugin()
    {
        using var harness = new DownloadHarness();
        harness.Loader
            .Add(FakePlugin.Returning("alpha", [Tx(institution: "alpha")]))
            .Add(FakePlugin.Returning("bravo", [Tx(institution: "bravo")]));

        var (exitCode, _, _) = await RunAsync(harness, new CommandLineOptions("bravo", false), Runnable("alpha"), Runnable("bravo"));

        Assert.Equal(0, exitCode);
        Assert.Equal(["bravo"], harness.Loader.LoadedNames);
    }

    [Fact]
    public async Task UnknownPlugin_ReturnsOne_WithTheSelectorMessage_AndRunsNothing()
    {
        using var harness = new DownloadHarness();

        var (exitCode, output, error) = await RunAsync(harness, new CommandLineOptions("nope", false), Runnable("alpha"));

        Assert.Equal(1, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Plugin 'nope' was not found. Installed plugins: alpha.", error, StringComparison.Ordinal);
        Assert.Equal(0, harness.Loader.LoadCalls);
    }

    [Fact]
    public async Task ExplicitlyRequestedDescriptorOnlyPlugin_ReturnsOne_WithAClearMessage()
    {
        using var harness = new DownloadHarness();

        var (exitCode, output, error) = await RunAsync(harness, new CommandLineOptions("notes", false), DescriptorOnly("notes"));

        Assert.Equal(1, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Plugin 'notes' cannot be downloaded: declares no executable assembly.", error, StringComparison.Ordinal);
        Assert.Equal(0, harness.Loader.LoadCalls);
    }

    [Fact]
    public async Task DescriptorOnlyPlugin_InAnAllPluginsRun_IsListedAsNotRunnable_AndExitsZero()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx()]));

        var (exitCode, output, error) = await RunAsync(harness, Table, Runnable("fidelity"), DescriptorOnly("notes"));

        Assert.Equal(0, exitCode);
        Assert.Contains("not-runnable", output, StringComparison.Ordinal);
        Assert.Contains("notes", output, StringComparison.Ordinal);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task NoPlugins_PrintsTheMessage_ReturnsZero_AndLeavesTheLedgerAlone()
    {
        using var harness = new DownloadHarness();

        var (exitCode, output, error) = await RunAsync(harness, Table);

        Assert.Equal(0, exitCode);
        Assert.Equal("No plugins installed." + Environment.NewLine, output);
        Assert.Equal("", error);
        Assert.False(harness.Ledger.Database.Exists);
    }

    [Fact]
    public async Task CancelledToken_Propagates()
    {
        using var harness = new DownloadHarness();
        harness.Loader.Add(FakePlugin.Returning("fidelity", [Tx()]));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var command = new DownloadCommand(new StubPluginCatalog(Runnable("fidelity")), harness.Service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            command.ExecuteAsync(Table, new StringWriter(), new StringWriter(), cts.Token));
    }
}
