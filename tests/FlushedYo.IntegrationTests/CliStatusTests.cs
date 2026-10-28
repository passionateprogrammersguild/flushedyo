using System.Text.Json;

namespace FlushedYo.IntegrationTests;

/// <summary>End-to-end <c>status</c> tests against the built CLI.</summary>
public sealed class CliStatusTests
{
    [Fact]
    public async Task FreshMachine_ReportsNeverRun_NoLedger()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("never", result.StdOut);
        Assert.Contains("0", result.StdOut);
        Assert.False(workspace.LedgerExists);
        Assert.False(workspace.LedgerDirectoryExists);
    }

    [Fact]
    public async Task AfterDownload_ShowsCountsAndRun()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));
        await workspace.RunAsync(["download"]);

        var result = await workspace.RunAsync(["status", "--json"]);
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.StdOut);
        var plugin = doc.RootElement.GetProperty("plugins")[0];
        Assert.Equal("succeeded", plugin.GetProperty("lastRun").GetProperty("outcome").GetString());
        Assert.Equal(3, plugin.GetProperty("lastRun").GetProperty("inserted").GetInt32());
        Assert.Equal(3, plugin.GetProperty("storedTransactions").GetInt64());
        var finishedAt = plugin.GetProperty("lastRun").GetProperty("finishedAt").GetString()!;
        var parsed = DateTimeOffset.Parse(finishedAt, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, parsed.Offset); // UTC
    }

    [Fact]
    public async Task SecondDownload_UpdatesLastRun()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        await workspace.RunAsync(["download", "--json"]);
        var status1 = await workspace.RunAsync(["status", "--json"]);
        using var doc1 = JsonDocument.Parse(status1.StdOut);
        var run1 = doc1.RootElement.GetProperty("plugins")[0].GetProperty("lastRun");
        Assert.Equal(3, run1.GetProperty("inserted").GetInt32());
        Assert.Equal(0, run1.GetProperty("skipped").GetInt32());

        await workspace.RunAsync(["download", "--json"]);
        var status2 = await workspace.RunAsync(["status", "--json"]);
        using var doc2 = JsonDocument.Parse(status2.StdOut);
        var run2 = doc2.RootElement.GetProperty("plugins")[0].GetProperty("lastRun");
        Assert.Equal(0, run2.GetProperty("inserted").GetInt32());
        Assert.Equal(3, run2.GetProperty("skipped").GetInt32());
        Assert.Equal(3, doc2.RootElement.GetProperty("plugins")[0].GetProperty("storedTransactions").GetInt64()); // unchanged
    }

    [Fact]
    public async Task AfterFailedRun_ReportsFailure()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: """{ "throw": "boom" }""");
        await workspace.RunAsync(["download", "--json"]);

        var result = await workspace.RunAsync(["status", "--json"]);
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.StdOut);
        var plugin = doc.RootElement.GetProperty("plugins")[0];
        Assert.Equal("failed", plugin.GetProperty("lastRun").GetProperty("outcome").GetString());
        Assert.Equal("Plugin threw InvalidOperationException.", plugin.GetProperty("lastRun").GetProperty("error").GetString());
    }

    [Fact]
    public async Task DashPlugin_Filter_ShowsOnlyThatPlugin()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));
        workspace.AddStubPlugin("bravo", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["status", "--plugin", "alpha", "--json"]);
        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.StdOut);
        var plugins = doc.RootElement.GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("plugin").GetString()).ToArray();
        Assert.Equal(["alpha"], plugins);
    }

    [Fact]
    public async Task UnknownPlugin_ExitsOne()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["status", "--plugin", "nope"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("was not found", result.StdErr);
    }

    [Fact]
    public async Task UninstalledInstitution_ShowsWithStoredRows()
    {
        using var workspace = new CliWorkspace();
        var folder = workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));
        await workspace.RunAsync(["download", "--json"]);
        workspace.RemovePlugin("alpha");

        var result = await workspace.RunAsync(["status"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("alpha", result.StdOut);
        Assert.Contains("not installed", result.StdOut);
        Assert.Contains("3", result.StdOut);

        // --plugin alpha is now a usage error.
        var targeted = await workspace.RunAsync(["status", "--plugin", "alpha"]);
        Assert.Equal(1, targeted.ExitCode);
    }

    [Fact]
    public async Task Status_Is_ReadOnly()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));
        await workspace.RunAsync(["download", "--json"]);

        var first = await workspace.RunAsync(["status", "--json"]);
        var second = await workspace.RunAsync(["status", "--json"]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.StdOut, second.StdOut); // identical JSON
    }
}
