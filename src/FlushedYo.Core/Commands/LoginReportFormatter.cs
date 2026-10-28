namespace FlushedYo.Core.Commands;

/// <summary>Renders a <see cref="LoginReport"/> as a table or JSON. Output holds key names and statuses only.</summary>
public static class LoginReportFormatter
{
    private const string EmptyMessage = "No plugins installed.";

    private static readonly string[] Headers = ["PLUGIN", "CREDENTIAL", "KEY", "STATUS"];

    /// <summary>Formats the report as a table with one row per credential.</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>The table, or <c>No plugins installed.</c> when the report is empty.</returns>
    public static string FormatTable(LoginReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Plugins.Count == 0)
        {
            return EmptyMessage;
        }

        var rows = report.Plugins
            .SelectMany(static plugin => plugin.Credentials.Select(check =>
                (IReadOnlyList<string>)[plugin.Plugin, check.Credential, check.Key ?? "-", StatusText(check.Status)]))
            .ToList();

        return TextTable.Render(Headers, rows);
    }

    /// <summary>Formats the report as JSON.</summary>
    /// <param name="report">The report to render.</param>
    /// <returns>The JSON document without a trailing newline.</returns>
    public static string FormatJson(LoginReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return CliJson.Serialize(new LoginJson(
            !report.HasMissing,
            [.. report.Plugins.Select(static plugin => new PluginJson(
                plugin.Plugin,
                [.. plugin.Credentials.Select(static check => new CredentialJson(check.Credential, check.Key, check.Status))]))]));
    }

    private static string StatusText(CredentialStatus status) => status switch
    {
        CredentialStatus.Ok => "ok",
        CredentialStatus.Missing => "missing",
        CredentialStatus.NotConfigured => "not-configured",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown credential status."),
    };

    private sealed record LoginJson(bool Ok, IReadOnlyList<PluginJson> Plugins);

    private sealed record PluginJson(string Plugin, IReadOnlyList<CredentialJson> Credentials);

    private sealed record CredentialJson(string Credential, string? Key, CredentialStatus Status);
}
