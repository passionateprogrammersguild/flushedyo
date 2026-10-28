using FlushedYo.Core.Plugin;
using FlushedYo.Core.Store;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Builds the <c>status</c> report from run history and stored totals. Read-only: it never opens the
/// ledger itself, and the history store returns empty results (without creating anything) when the
/// ledger does not exist yet.
/// </summary>
public sealed class StatusService(IRunHistoryStore history)
{
    /// <summary>Reports each plugin's last run and stored transaction count.</summary>
    /// <param name="plugins">The installed plugins to report, in the order to show them.</param>
    /// <param name="includeUninstalled">
    /// When true, also report institutions that have stored rows or runs but no installed plugin
    /// (sorted by name, after the installed plugins).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the queries.</param>
    /// <returns>The report.</returns>
    public async Task<StatusReport> GetAsync(
        IReadOnlyList<PluginDescriptor> plugins,
        bool includeUninstalled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        cancellationToken.ThrowIfCancellationRequested();

        // Normalize to case-insensitive lookups here so the report does not depend on how a store keyed them.
        var lastRuns = new Dictionary<string, DownloadRun>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, run) in await history.GetLastRunsAsync(cancellationToken).ConfigureAwait(false))
        {
            lastRuns[name] = run;
        }

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (institution, count) in await history.GetStoredTransactionCountsAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[institution] = count;
        }

        var statuses = plugins
            .Select(plugin => new PluginStatus(
                plugin.Name,
                plugin.Version,
                Installed: true,
                lastRuns.GetValueOrDefault(plugin.Name),
                counts.GetValueOrDefault(plugin.Name)))
            .ToList();

        if (includeUninstalled)
        {
            var installedNames = new HashSet<string>(plugins.Select(static plugin => plugin.Name), StringComparer.OrdinalIgnoreCase);

            // Prefer a run's original-case plugin name over the lower-cased institution key.
            var uninstalled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var run in lastRuns.Values.Where(run => !installedNames.Contains(run.Plugin)))
            {
                uninstalled[run.Plugin] = run.Plugin;
            }

            foreach (var institution in counts.Keys.Where(institution => !installedNames.Contains(institution)))
            {
                uninstalled.TryAdd(institution, institution);
            }

            statuses.AddRange(uninstalled.Values
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new PluginStatus(
                    name,
                    lastRuns.GetValueOrDefault(name)?.PluginVersion,
                    Installed: false,
                    lastRuns.GetValueOrDefault(name),
                    counts.GetValueOrDefault(name))));
        }

        return new StatusReport(statuses);
    }
}
