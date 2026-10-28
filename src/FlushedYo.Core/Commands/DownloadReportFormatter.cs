using System.Globalization;

namespace FlushedYo.Core.Commands;

/// <summary>Renders a <see cref="DownloadReport"/> as a table or JSON.</summary>
public static class DownloadReportFormatter
{
    private const string EmptyMessage = "No plugins installed.";

    private static readonly string[] Headers = ["PLUGIN", "STATUS", "INSERTED", "SKIPPED", "REJECTED", "DETAIL"];

    /// <summary>Formats the report as a table with one row per plugin.</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>The table, or <c>No plugins installed.</c> when the report is empty.</returns>
    public static string FormatTable(DownloadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Plugins.Count == 0)
        {
            return EmptyMessage;
        }

        var rows = report.Plugins
            .Select(static result =>
            {
                var notRunnable = result.Status == DownloadStatus.NotRunnable;
                return (IReadOnlyList<string>)
                [
                    result.Plugin,
                    StatusText(result.Status),
                    notRunnable ? "-" : result.Inserted.ToString(CultureInfo.InvariantCulture),
                    notRunnable ? "-" : result.Skipped.ToString(CultureInfo.InvariantCulture),
                    notRunnable ? "-" : result.Rejected.ToString(CultureInfo.InvariantCulture),
                    result.Detail ?? "-",
                ];
            })
            .ToList();

        return TextTable.Render(Headers, rows);
    }

    /// <summary>Formats the report as JSON. Counts are <see langword="null"/> for plugins that were not runnable.</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>The JSON document without a trailing newline.</returns>
    public static string FormatJson(DownloadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return CliJson.Serialize(new DownloadJson(
            report.SucceededCount,
            report.FailedCount,
            report.NotRunnableCount,
            [.. report.Plugins.Select(static result =>
            {
                var notRunnable = result.Status == DownloadStatus.NotRunnable;
                return new PluginJson(
                    result.Plugin,
                    result.Status,
                    notRunnable ? null : result.Inserted,
                    notRunnable ? null : result.Skipped,
                    notRunnable ? null : result.Rejected,
                    result.Detail);
            })]));
    }

    private static string StatusText(DownloadStatus status) => status switch
    {
        DownloadStatus.Succeeded => "succeeded",
        DownloadStatus.Failed => "failed",
        DownloadStatus.NotRunnable => "not-runnable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown download status."),
    };

    private sealed record DownloadJson(int Succeeded, int Failed, int NotRunnable, IReadOnlyList<PluginJson> Plugins);

    private sealed record PluginJson(string Plugin, DownloadStatus Status, int? Inserted, int? Skipped, int? Rejected, string? Detail);
}
