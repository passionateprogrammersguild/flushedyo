using System.Text.Json;
using System.Text.RegularExpressions;
using FlushedYo.Core.Commands;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Table and JSON rendering for download (spec: download_service_and_command).</summary>
public sealed partial class DownloadReportFormatterTests
{
    private static DownloadReport Report() => new(
    [
        new PluginDownloadResult("fidelity", DownloadStatus.Succeeded, 3, 0, 0, null),
        new PluginDownloadResult("schwab", DownloadStatus.Failed, 0, 0, 0, "Plugin threw InvalidOperationException."),
        new PluginDownloadResult("notes", DownloadStatus.NotRunnable, 0, 0, 0, "declares no executable assembly"),
    ]);

    [GeneratedRegex(" {2,}")]
    private static partial Regex ColumnGap();

    private static string[] Cells(string line) => ColumnGap().Split(line);

    [Fact]
    public void EmptyReport_RendersTheNoPluginsMessage()
    {
        Assert.Equal("No plugins installed.", DownloadReportFormatter.FormatTable(new DownloadReport([])));
    }

    [Fact]
    public void Table_RendersStatuses_DashesForNotRunnableCounts_AndDashForNoDetail()
    {
        var lines = DownloadReportFormatter.FormatTable(Report()).Split('\n');

        Assert.Equal(["PLUGIN", "STATUS", "INSERTED", "SKIPPED", "REJECTED", "DETAIL"], Cells(lines[0]));
        Assert.Equal(["fidelity", "succeeded", "3", "0", "0", "-"], Cells(lines[1]));
        Assert.Equal(["schwab", "failed", "0", "0", "0", "Plugin threw InvalidOperationException."], Cells(lines[2]));
        Assert.Equal(["notes", "not-runnable", "-", "-", "-", "declares no executable assembly"], Cells(lines[3]));
    }

    [Fact]
    public void Table_HasNoTrailingWhitespaceOrNewline()
    {
        var table = DownloadReportFormatter.FormatTable(Report());

        Assert.False(table.EndsWith('\n'));
        Assert.All(table.Split('\n'), line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void Json_HasTheDocumentedShape()
    {
        using var document = JsonDocument.Parse(DownloadReportFormatter.FormatJson(Report()));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("notRunnable").GetInt32());

        var plugins = root.GetProperty("plugins");
        Assert.Equal("succeeded", plugins[0].GetProperty("status").GetString());
        Assert.Equal(3, plugins[0].GetProperty("inserted").GetInt32());
        Assert.Equal(JsonValueKind.Null, plugins[0].GetProperty("detail").ValueKind);

        Assert.Equal("failed", plugins[1].GetProperty("status").GetString());
        Assert.Equal("Plugin threw InvalidOperationException.", plugins[1].GetProperty("detail").GetString());

        Assert.Equal("notRunnable", plugins[2].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, plugins[2].GetProperty("inserted").ValueKind);
        Assert.Equal(JsonValueKind.Null, plugins[2].GetProperty("skipped").ValueKind);
        Assert.Equal(JsonValueKind.Null, plugins[2].GetProperty("rejected").ValueKind);
        Assert.Equal("declares no executable assembly", plugins[2].GetProperty("detail").GetString());
    }

    [Fact]
    public void Json_ForAnEmptyReport_HasZeroCountsAndNoPlugins()
    {
        using var document = JsonDocument.Parse(DownloadReportFormatter.FormatJson(new DownloadReport([])));

        Assert.Equal(0, document.RootElement.GetProperty("succeeded").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("plugins").GetArrayLength());
    }

    [Fact]
    public void Reports_CountEachStatus()
    {
        var report = Report();

        Assert.Equal(1, report.SucceededCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(1, report.NotRunnableCount);
    }
}
