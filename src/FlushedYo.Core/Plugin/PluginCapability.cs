namespace FlushedYo.Core.Plugin;

/// <summary>
/// Capabilities a plugin can declare in its <c>plugin.json</c> manifest.
/// </summary>
public enum PluginCapability
{
    /// <summary>The plugin can download transactions (<c>download-transactions</c>).</summary>
    DownloadTransactions,

    /// <summary>The plugin can download holdings (<c>download-holdings</c>).</summary>
    DownloadHoldings,
}

/// <summary>
/// Maps <see cref="PluginCapability"/> values to and from the strings used in <c>plugin.json</c>.
/// Matching is case-sensitive and exact.
/// </summary>
public static class PluginCapabilityNames
{
    private const string DownloadTransactionsName = "download-transactions";
    private const string DownloadHoldingsName = "download-holdings";

    /// <summary>Parses a manifest capability string.</summary>
    /// <param name="value">Capability string from the manifest.</param>
    /// <param name="capability">The parsed capability when <paramref name="value"/> is known.</param>
    /// <returns><see langword="true"/> when the value is a known capability.</returns>
    public static bool TryParse(string? value, out PluginCapability capability)
    {
        switch (value)
        {
            case DownloadTransactionsName:
                capability = PluginCapability.DownloadTransactions;
                return true;
            case DownloadHoldingsName:
                capability = PluginCapability.DownloadHoldings;
                return true;
            default:
                capability = default;
                return false;
        }
    }

    /// <summary>Returns the manifest string for <paramref name="capability"/>.</summary>
    /// <param name="capability">Capability to render.</param>
    /// <returns>The manifest string, for example <c>download-transactions</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capability"/> is not a known value.</exception>
    public static string ToManifestName(PluginCapability capability) => capability switch
    {
        PluginCapability.DownloadTransactions => DownloadTransactionsName,
        PluginCapability.DownloadHoldings => DownloadHoldingsName,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown plugin capability."),
    };
}
