namespace FlushedYo.Core.Store;

/// <summary>How a download run for one plugin ended.</summary>
public enum DownloadOutcome
{
    /// <summary>The plugin ran and its transactions were tracked.</summary>
    Succeeded,

    /// <summary>The plugin could not be loaded, or threw while downloading.</summary>
    Failed,
}

/// <summary>One attempted download of one plugin, recorded for <c>status</c>.</summary>
/// <param name="Plugin">Plugin name.</param>
/// <param name="PluginVersion">Version of the plugin that ran (or, for a load failure, the manifest version).</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="FinishedAt">When the run finished. Never earlier than <paramref name="StartedAt"/>.</param>
/// <param name="Outcome">Whether the run succeeded.</param>
/// <param name="Inserted">New transactions written to the ledger.</param>
/// <param name="Skipped">Transactions that were already stored.</param>
/// <param name="Rejected">Transactions that were invalid or did not belong to the plugin, and were not stored.</param>
/// <param name="Error">
/// A value-free error message, required when <paramref name="Outcome"/> is <see cref="DownloadOutcome.Failed"/>
/// and <see langword="null"/> otherwise.
/// </param>
public sealed record DownloadRun(
    string Plugin,
    string PluginVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    DownloadOutcome Outcome,
    int Inserted,
    int Skipped,
    int Rejected,
    string? Error);
