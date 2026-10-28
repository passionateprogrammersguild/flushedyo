namespace FlushedYo.Core.Plugin;

/// <summary>
/// Loads plugins once on first use and serves the cached result afterwards.
/// </summary>
public sealed class PluginCatalog : IPluginCatalog
{
    private readonly Lazy<Task<IReadOnlyList<PluginDescriptor>>> _plugins;

    /// <summary>Creates a catalog backed by <paramref name="loader"/>.</summary>
    /// <param name="loader">Loader that scans the plugin directory.</param>
    public PluginCatalog(PluginLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _plugins = new Lazy<Task<IReadOnlyList<PluginDescriptor>>>(() => loader.LoadAsync(CancellationToken.None));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginDescriptor>> GetPluginsAsync(CancellationToken cancellationToken = default)
    {
        // WaitAsync returns an already-completed task without observing the token.
        cancellationToken.ThrowIfCancellationRequested();
        return _plugins.Value.WaitAsync(cancellationToken);
    }
}
