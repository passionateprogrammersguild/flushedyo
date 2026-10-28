using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Commands;

/// <summary>
/// <c>flushedyo status [--plugin &lt;name&gt;] [--json]</c>: shows each plugin's last download and stored totals.
/// </summary>
/// <remarks>
/// Read-only and informational. Exit code <c>0</c> regardless of how the last run ended, <c>1</c> for an unknown
/// plugin. Ledger errors propagate so the host can map them to exit code <c>2</c>. Without <c>--plugin</c> it also
/// lists institutions that have stored data but no installed plugin.
/// </remarks>
public sealed class StatusCommand(IPluginCatalog catalog, StatusService service)
{
    /// <summary>Runs the command.</summary>
    /// <param name="options">Parsed flags.</param>
    /// <param name="output">Receives the report.</param>
    /// <param name="error">Receives usage errors.</param>
    /// <param name="cancellationToken">Token that cancels the command.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> ExecuteAsync(
        CommandLineOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var installed = await catalog.GetPluginsAsync(cancellationToken).ConfigureAwait(false);
        var selection = PluginSelector.Select(installed, options.Plugin);
        if (selection.Error is not null)
        {
            await error.WriteLineAsync(selection.Error).ConfigureAwait(false);
            return 1;
        }

        var report = await service.GetAsync(selection.Plugins, includeUninstalled: options.Plugin is null, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(options.Json ? StatusReportFormatter.FormatJson(report) : StatusReportFormatter.FormatTable(report))
            .ConfigureAwait(false);

        return 0;
    }
}
