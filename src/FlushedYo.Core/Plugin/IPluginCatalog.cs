namespace FlushedYo.Core.Plugin;

/// <summary>
/// Provides the plugins discovered in the configured plugin directory.
/// </summary>
public interface IPluginCatalog
{
    /// <summary>Returns the discovered plugins, sorted by name.</summary>
    /// <param name="cancellationToken">Token that cancels the wait for the result.</param>
    /// <returns>The valid plugins. Invalid plugins are skipped with a warning.</returns>
    Task<IReadOnlyList<PluginDescriptor>> GetPluginsAsync(CancellationToken cancellationToken = default);
}
