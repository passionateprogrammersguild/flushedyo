using System.Globalization;
using FlushedYo.Core.Store;

namespace FlushedYo.Core.Commands;

/// <summary>Renders a <see cref="StatusReport"/> as a table or JSON.</summary>
public static class StatusReportFormatter
{
    private const string EmptyMessage = "No plugins installed.";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string[] Headers =
        ["PLUGIN", "VERSION", "LAST RUN", "OUTCOME", "INSERTED", "SKIPPED", "REJECTED", "STORED", "ERROR"];

    /// <summary>Formats the report as a table with one row per plugin.</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>The table, or <c>No plugins installed.</c> when the report is empty.</returns>
    public static string FormatTable(StatusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Plugins.Count == 0)
        {
            return EmptyMessage;
        }

        var rows = report.Plugins
            .Select(static status =>
            {
                var run = status.LastRun;
                return (IReadOnlyList<string>)
                [
                    status.Installed ? status.Name : $"{status.Name} (not installed)",
                    status.Version ?? "-",
                    run is null ? "never" : run.FinishedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                    run is null ? "-" : OutcomeText(run.Outcome),
                    Count(run?.Inserted),
                    Count(run?.Skipped),
                    Count(run?.Rejected),
                    status.StoredTransactions.ToString(CultureInfo.InvariantCulture),
                    run?.Error ?? "-",
                ];
            })
            .ToList();

        return TextTable.Render(Headers, rows);
    }

    /// <summary>Formats the report as JSON. <c>lastRun</c> is <see langword="null"/> for a plugin that never ran.</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>The JSON document without a trailing newline.</returns>
    public static string FormatJson(StatusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return CliJson.Serialize(new StatusJson(
            [.. report.Plugins.Select(static status => new PluginJson(
                status.Name,
                status.Version,
                status.Installed,
                status.StoredTransactions,
                status.LastRun is null ? null : ToJson(status.LastRun)))]));
    }

    private static RunJson ToJson(DownloadRun run) => new(
        run.StartedAt.ToUniversalTime(),
        run.FinishedAt.ToUniversalTime(),
        run.PluginVersion,
        run.Outcome,
        run.Inserted,
        run.Skipped,
        run.Rejected,
        run.Error);

    private static string Count(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string OutcomeText(DownloadOutcome outcome) => outcome switch
    {
        DownloadOutcome.Succeeded => "succeeded",
        DownloadOutcome.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown download outcome."),
    };

    private sealed record StatusJson(IReadOnlyList<PluginJson> Plugins);

    private sealed record PluginJson(string Plugin, string? Version, bool Installed, long StoredTransactions, RunJson? LastRun);

    private sealed record RunJson(
        DateTimeOffset StartedAt,
        DateTimeOffset FinishedAt,
        string PluginVersion,
        DownloadOutcome Outcome,
        int Inserted,
        int Skipped,
        int Rejected,
        string? Error);
}
