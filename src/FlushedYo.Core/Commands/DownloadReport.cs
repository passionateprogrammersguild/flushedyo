namespace FlushedYo.Core.Commands;

/// <summary>How <c>download</c> treated one plugin.</summary>
public enum DownloadStatus
{
    /// <summary>The plugin ran and its transactions were tracked.</summary>
    Succeeded,

    /// <summary>The plugin could not be loaded, or threw while downloading.</summary>
    Failed,

    /// <summary>The plugin cannot be run (no executable assembly, or no download capability); it was not attempted.</summary>
    NotRunnable,
}

/// <summary>The outcome of downloading one plugin.</summary>
/// <param name="Plugin">Plugin name.</param>
/// <param name="Status">What happened.</param>
/// <param name="Inserted">New transactions stored. Zero unless <paramref name="Status"/> is <see cref="DownloadStatus.Succeeded"/>.</param>
/// <param name="Skipped">Transactions already stored. Zero unless the run succeeded.</param>
/// <param name="Rejected">Transactions that were invalid or did not belong to the plugin. Zero unless the run succeeded.</param>
/// <param name="Detail">
/// A value-free error for <see cref="DownloadStatus.Failed"/>, the reason for <see cref="DownloadStatus.NotRunnable"/>,
/// and <see langword="null"/> for <see cref="DownloadStatus.Succeeded"/>.
/// </param>
public sealed record PluginDownloadResult(
    string Plugin,
    DownloadStatus Status,
    int Inserted,
    int Skipped,
    int Rejected,
    string? Detail);

/// <summary>The outcome of <c>download</c> across the selected plugins.</summary>
/// <param name="Plugins">One result per plugin, in the order processed.</param>
public sealed record DownloadReport(IReadOnlyList<PluginDownloadResult> Plugins)
{
    /// <summary>Number of plugins that succeeded.</summary>
    public int SucceededCount => Count(DownloadStatus.Succeeded);

    /// <summary>Number of plugins that failed.</summary>
    public int FailedCount => Count(DownloadStatus.Failed);

    /// <summary>Number of plugins that were not runnable.</summary>
    public int NotRunnableCount => Count(DownloadStatus.NotRunnable);

    private int Count(DownloadStatus status) => Plugins.Count(result => result.Status == status);
}
