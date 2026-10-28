namespace FlushedYo.Core.Plugin;

/// <summary>The plugins a command should act on, or why none could be chosen.</summary>
/// <param name="Plugins">The selected plugins; empty when <paramref name="Error"/> is set.</param>
/// <param name="Error">A usage message when the requested plugin does not exist; otherwise <see langword="null"/>.</param>
public sealed record PluginSelection(IReadOnlyList<PluginDescriptor> Plugins, string? Error);

/// <summary>Chooses plugins by the <c>--plugin</c> value.</summary>
public static class PluginSelector
{
    /// <summary>Selects all installed plugins, or the one named <paramref name="name"/>.</summary>
    /// <param name="installed">The discovered plugins, in the order commands should process them.</param>
    /// <param name="name">The <c>--plugin</c> value, matched case-insensitively; <see langword="null"/> selects everything.</param>
    /// <returns>The selection, with an <see cref="PluginSelection.Error"/> when <paramref name="name"/> matches nothing.</returns>
    public static PluginSelection Select(IReadOnlyList<PluginDescriptor> installed, string? name)
    {
        ArgumentNullException.ThrowIfNull(installed);

        if (name is null)
        {
            return new PluginSelection(installed, null);
        }

        var match = installed.FirstOrDefault(plugin => string.Equals(plugin.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return new PluginSelection([match], null);
        }

        var available = installed.Count == 0
            ? "No plugins are installed."
            : "Installed plugins: " + string.Join(", ", installed.Select(static plugin => plugin.Name)) + ".";

        return new PluginSelection([], $"Plugin '{name}' was not found. {available}");
    }
}
