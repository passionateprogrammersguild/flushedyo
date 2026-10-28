using FlushedYo.Core.Store;

namespace FlushedYo.Core.Commands;

/// <summary>What <c>status</c> knows about one plugin (or one institution whose plugin is gone).</summary>
/// <param name="Name">Plugin name, or the institution name for a plugin that is no longer installed.</param>
/// <param name="Version">
/// The manifest version for an installed plugin. For a plugin that is not installed, the version from its
/// last run, or <see langword="null"/> when it never ran.
/// </param>
/// <param name="Installed">False for an institution that has stored rows or runs but no installed plugin.</param>
/// <param name="LastRun">The most recent download run of any outcome, or <see langword="null"/> if it never ran.</param>
/// <param name="StoredTransactions">How many transactions are stored for this plugin's institution.</param>
public sealed record PluginStatus(string Name, string? Version, bool Installed, DownloadRun? LastRun, long StoredTransactions);

/// <summary>The outcome of <c>status</c>.</summary>
/// <param name="Plugins">Installed plugins in the order given, followed by any not-installed institutions.</param>
public sealed record StatusReport(IReadOnlyList<PluginStatus> Plugins);
