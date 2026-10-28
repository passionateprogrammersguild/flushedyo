using System.Text.Json;
using System.Text.RegularExpressions;
using FlushedYo.Core.Commands;
using FlushedYo.Core.Store;
using static FlushedYo.Core.Tests.Store.RunTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Table and JSON rendering for status (spec: status_service_and_command).</summary>
public sealed partial class StatusReportFormatterTests
{
    [GeneratedRegex(" {2,}")]
    private static partial Regex ColumnGap();

    private static string[] Cells(string line) => ColumnGap().Split(line);

    private static string[] Lines(StatusReport report) => StatusReportFormatter.FormatTable(report).Split('\n');

    [Fact]
    public void EmptyReport_RendersTheNoPluginsMessage()
    {
        Assert.Equal("No plugins installed.", StatusReportFormatter.FormatTable(new StatusReport([])));
    }

    [Fact]
    public void Header_ListsTheColumns()
    {
        var lines = Lines(new StatusReport([new PluginStatus("alpha", "1.0.0", true, null, 0)]));

        Assert.Equal(
            ["PLUGIN", "VERSION", "LAST RUN", "OUTCOME", "INSERTED", "SKIPPED", "REJECTED", "STORED", "ERROR"],
            Cells(lines[0]));
    }

    [Fact]
    public void ANeverRunPlugin_ShowsNeverAndDashes_WithZeroStored()
    {
        var lines = Lines(new StatusReport([new PluginStatus("alpha", "1.0.0", true, null, 0)]));

        Assert.Equal(["alpha", "1.0.0", "never", "-", "-", "-", "-", "0", "-"], Cells(lines[1]));
    }

    [Fact]
    public void ASuccessfulRun_ShowsTheFinishTimeInUtc_AndTheCounts()
    {
        var finished = new DateTimeOffset(2026, 3, 4, 10, 6, 9, TimeSpan.FromHours(5));
        var run = Run(plugin: "fidelity", startedAt: finished.AddSeconds(-4), finishedAt: finished, inserted: 3, skipped: 2, rejected: 1);

        var lines = Lines(new StatusReport([new PluginStatus("fidelity", "1.0.0", true, run, 3)]));

        Assert.Equal(["fidelity", "1.0.0", "2026-03-04T05:06:09Z", "succeeded", "3", "2", "1", "3", "-"], Cells(lines[1]));
    }

    [Fact]
    public void AFailedRun_ShowsFailedAndTheError()
    {
        var run = Run(outcome: DownloadOutcome.Failed, inserted: 0, skipped: 0, rejected: 0, error: "Plugin threw InvalidOperationException.");

        var lines = Lines(new StatusReport([new PluginStatus("stub", "1.0.0", true, run, 0)]));

        var cells = Cells(lines[1]);
        Assert.Equal("failed", cells[3]);
        Assert.Equal("Plugin threw InvalidOperationException.", cells[8]);
    }

    [Fact]
    public void ANotInstalledPlugin_IsLabelled_AndAMissingVersionIsADash()
    {
        var lines = Lines(new StatusReport([new PluginStatus("gone", null, false, null, 4)]));

        Assert.Equal(["gone (not installed)", "-", "never", "-", "-", "-", "-", "4", "-"], Cells(lines[1]));
    }

    [Fact]
    public void LargeStoredCounts_UseInvariantFormatting()
    {
        var lines = Lines(new StatusReport([new PluginStatus("alpha", "1.0.0", true, null, 1234567)]));

        Assert.Equal("1234567", Cells(lines[1])[7]);
    }

    [Fact]
    public void Table_HasNoTrailingWhitespaceOrNewline()
    {
        var table = StatusReportFormatter.FormatTable(new StatusReport(
        [
            new PluginStatus("alpha", "1.0.0", true, Run(plugin: "alpha"), 3),
            new PluginStatus("gone", null, false, null, 0),
        ]));

        Assert.False(table.EndsWith('\n'));
        Assert.All(table.Split('\n'), line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void Json_HasTheDocumentedShape()
    {
        var run = Run(plugin: "fidelity", inserted: 3, skipped: 0, rejected: 0);
        var report = new StatusReport(
        [
            new PluginStatus("fidelity", "1.0.0", true, run, 3),
            new PluginStatus("schwab", "0.1.0", true, null, 0),
        ]);

        using var document = JsonDocument.Parse(StatusReportFormatter.FormatJson(report));
        var plugins = document.RootElement.GetProperty("plugins");

        var first = plugins[0];
        Assert.Equal("fidelity", first.GetProperty("plugin").GetString());
        Assert.Equal("1.0.0", first.GetProperty("version").GetString());
        Assert.True(first.GetProperty("installed").GetBoolean());
        Assert.Equal(3, first.GetProperty("storedTransactions").GetInt64());

        var lastRun = first.GetProperty("lastRun");
        Assert.Equal("succeeded", lastRun.GetProperty("outcome").GetString());
        Assert.Equal(3, lastRun.GetProperty("inserted").GetInt32());
        Assert.Equal(0, lastRun.GetProperty("skipped").GetInt32());
        Assert.Equal(JsonValueKind.Null, lastRun.GetProperty("error").ValueKind);
        Assert.Equal(DefaultStart.AddSeconds(5).UtcDateTime, lastRun.GetProperty("finishedAt").GetDateTimeOffset().UtcDateTime);
        Assert.Equal("1.0.0", lastRun.GetProperty("pluginVersion").GetString());

        Assert.Equal(JsonValueKind.Null, plugins[1].GetProperty("lastRun").ValueKind);
    }

    [Fact]
    public void Json_FailedRun_CarriesTheErrorAndCamelCaseOutcome()
    {
        var run = Run(outcome: DownloadOutcome.Failed, error: "Plugin threw InvalidOperationException.");

        using var document = JsonDocument.Parse(StatusReportFormatter.FormatJson(new StatusReport([new PluginStatus("stub", "1.0.0", true, run, 0)])));

        var lastRun = document.RootElement.GetProperty("plugins")[0].GetProperty("lastRun");
        Assert.Equal("failed", lastRun.GetProperty("outcome").GetString());
        Assert.Equal("Plugin threw InvalidOperationException.", lastRun.GetProperty("error").GetString());
    }

    [Fact]
    public void Json_NotInstalled_IsMarkedAndMayHaveANullVersion()
    {
        using var document = JsonDocument.Parse(StatusReportFormatter.FormatJson(new StatusReport([new PluginStatus("gone", null, false, null, 4)])));

        var plugin = document.RootElement.GetProperty("plugins")[0];
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plugin.GetProperty("version").ValueKind);
    }
}
