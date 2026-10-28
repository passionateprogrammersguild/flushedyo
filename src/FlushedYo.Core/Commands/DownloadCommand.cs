using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Commands;

/// <summary>
/// <c>flushedyo download [--plugin &lt;name&gt;] [--json]</c>: runs the selected plugins and stores their new
/// transactions.
/// </summary>
/// <remarks>
/// Exit codes: <c>0</c> when no plugin failed, <c>2</c> when any plugin failed, <c>1</c> for usage errors, an
/// unknown plugin, or an explicitly requested plugin that cannot be run. Cancellation and ledger errors
/// propagate so the host can map them (<c>130</c> and <c>2</c>).
/// </remarks>
public sealed class DownloadCommand(IPluginCatalog catalog, DownloadService service)
{
    /// <summary>Runs the command.</summary>
    /// <param name="options">Parsed flags.</param>
    /// <param name="output">Receives the report.</param>
    /// <param name="error">Receives usage errors and the failure hint.</param>
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

        if (options.Plugin is not null)
        {
            var plugin = selection.Plugins[0];
            var reason = DownloadService.GetNotRunnableReason(plugin);
            if (reason is not null)
            {
                await error.WriteLineAsync($"Plugin '{plugin.Name}' cannot be downloaded: {reason}.").ConfigureAwait(false);
                return 1;
            }
        }

        var report = await service.RunAsync(selection.Plugins, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(options.Json ? DownloadReportFormatter.FormatJson(report) : DownloadReportFormatter.FormatTable(report))
            .ConfigureAwait(false);

        if (report.FailedCount == 0)
        {
            return 0;
        }

        if (!options.Json)
        {
            await error.WriteLineAsync("One or more plugins failed.").ConfigureAwait(false);
        }

        return 2;
    }
}
