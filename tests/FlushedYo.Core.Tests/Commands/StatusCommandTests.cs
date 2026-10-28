using System.Text.Json;
using FlushedYo.Core.Commands;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tests.Store;
using static FlushedYo.Core.Tests.Commands.CommandTestData;
using static FlushedYo.Core.Tests.Store.RunTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Selection, output, and exit codes for status (spec: status_service_and_command).</summary>
public sealed class StatusCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly CommandLineOptions Table = new(null, false);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        CommandLineOptions options,
        IRunHistoryStore history,
        params PluginDescriptor[] plugins)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var command = new StatusCommand(new StubPluginCatalog(plugins), new StatusService(history));

        var exitCode = await command.ExecuteAsync(options, output, error, Token);

        return (exitCode, output.ToString(), error.ToString());
    }

    private static FakeRunHistory History() => new(
        [Run(plugin: "fidelity"), Run(plugin: "gone")],
        new Dictionary<string, long> { ["fidelity"] = 3, ["gone"] = 2 });

    [Fact]
    public async Task WithoutPluginFlag_IncludesNotInstalledInstitutions_AndReturnsZero()
    {
        var (exitCode, output, error) = await RunAsync(Table, History(), Descriptor("fidelity"));

        Assert.Equal(0, exitCode);
        Assert.Contains("fidelity", output, StringComparison.Ordinal);
        Assert.Contains("gone (not installed)", output, StringComparison.Ordinal);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task PluginFlag_ShowsOnlyThatPlugin_WithNoNotInstalledRows()
    {
        var (exitCode, output, _) = await RunAsync(new CommandLineOptions("fidelity", false), History(), Descriptor("fidelity"), Descriptor("schwab"));

        Assert.Equal(0, exitCode);
        Assert.Contains("fidelity", output, StringComparison.Ordinal);
        Assert.DoesNotContain("schwab", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not installed", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPlugin_ReturnsOne_WithTheSelectorMessageAndNoOutput()
    {
        var (exitCode, output, error) = await RunAsync(new CommandLineOptions("nope", false), History(), Descriptor("fidelity"));

        Assert.Equal(1, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Plugin 'nope' was not found. Installed plugins: fidelity.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonFlag_WritesAJsonDocument()
    {
        var (exitCode, output, error) = await RunAsync(new CommandLineOptions(null, true), History(), Descriptor("fidelity"));

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetProperty("plugins").GetArrayLength());
    }

    [Fact]
    public async Task AFailedLastRun_StillExitsZero()
    {
        var history = new FakeRunHistory([Run(plugin: "fidelity", outcome: DownloadOutcome.Failed, error: "Plugin threw InvalidOperationException.")]);

        var (exitCode, output, error) = await RunAsync(Table, history, Descriptor("fidelity"));

        Assert.Equal(0, exitCode);
        Assert.Contains("failed", output, StringComparison.Ordinal);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task NoPluginsAndAnEmptyLedger_PrintsTheMessage_AndReturnsZero()
    {
        using var ledger = new TempLedger();

        var (exitCode, output, error) = await RunAsync(Table, new SqliteRunHistoryStore(ledger.Database));

        Assert.Equal(0, exitCode);
        Assert.Equal("No plugins installed." + Environment.NewLine, output);
        Assert.Equal("", error);
        Assert.False(ledger.Database.Exists);
    }
}
