namespace FlushedYo.Core.Plugin;

/// <summary>
/// A running plugin together with the load context that holds its assembly. Disposing it disposes
/// the plugin and then unloads the context.
/// </summary>
public sealed class LoadedPlugin : IAsyncDisposable
{
    private readonly PluginLoadContext? _context;
    private int _disposed;

    // A null context means there is nothing to unload (used by tests that wrap an in-process plugin).
    internal LoadedPlugin(IProviderPlugin plugin, PluginLoadContext? context)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Plugin = plugin;
        _context = context;
    }

    /// <summary>The constructed plugin.</summary>
    public IProviderPlugin Plugin { get; }

    /// <summary>
    /// Disposes the plugin (<see cref="IAsyncDisposable"/> preferred, else <see cref="IDisposable"/>) and
    /// then unloads its context. Safe to call more than once. Unloading is cooperative and may complete
    /// later; callers must not wait for it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            switch (Plugin)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        finally
        {
            _context?.Unload();
        }
    }
}
