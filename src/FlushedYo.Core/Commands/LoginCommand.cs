using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Commands;

/// <summary>
/// <c>flushedyo login [--plugin &lt;name&gt;] [--json]</c>: a credential preflight. It reports whether each
/// selected plugin's credential keys exist in the secret store and persists nothing.
/// </summary>
/// <remarks>
/// Exit codes: <c>0</c> when no credential is missing, <c>1</c> for an unknown plugin or any missing credential.
/// Secret-store failures propagate so the host can map them to exit code <c>2</c>. This command never touches
/// the ledger.
/// </remarks>
public sealed class LoginCommand(IPluginCatalog catalog, LoginService service)
{
    /// <summary>Runs the command.</summary>
    /// <param name="options">Parsed flags.</param>
    /// <param name="output">Receives the report.</param>
    /// <param name="error">Receives usage errors and the missing-credentials hint.</param>
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

        var report = await service.CheckAsync(selection.Plugins, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(options.Json ? LoginReportFormatter.FormatJson(report) : LoginReportFormatter.FormatTable(report))
            .ConfigureAwait(false);

        if (!report.HasMissing)
        {
            return 0;
        }

        if (!options.Json)
        {
            await error.WriteLineAsync("Some credentials were not found in the secret store.").ConfigureAwait(false);
        }

        return 1;
    }
}
