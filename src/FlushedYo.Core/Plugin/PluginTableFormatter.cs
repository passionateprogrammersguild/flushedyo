using FlushedYo.Core.Commands;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// Renders discovered plugins as a plain-text table for <c>flushedyo plugins list</c>.
/// </summary>
public static class PluginTableFormatter
{
    private const string EmptyMessage = "No plugins installed.";

    private static readonly string[] Headers = ["NAME", "VERSION", "CAPABILITIES"];

    /// <summary>Formats <paramref name="plugins"/> as a table in the order given.</summary>
    /// <param name="plugins">Plugins to render; the loader already sorts them by name.</param>
    /// <returns>
    /// The table with LF line endings, no trailing newline, and no trailing whitespace,
    /// or <c>No plugins installed.</c> when the list is empty.
    /// </returns>
    public static string Format(IReadOnlyList<PluginDescriptor> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        if (plugins.Count == 0)
        {
            return EmptyMessage;
        }

        var rows = plugins
            .Select(static plugin => (IReadOnlyList<string>)[plugin.Name, plugin.Version, FormatCapabilities(plugin.Capabilities)])
            .ToList();

        return TextTable.Render(Headers, rows);
    }

    private static string FormatCapabilities(IReadOnlyList<PluginCapability> capabilities) =>
        capabilities.Count == 0
            ? "-"
            : string.Join(", ", capabilities.Select(PluginCapabilityNames.ToManifestName));
}
