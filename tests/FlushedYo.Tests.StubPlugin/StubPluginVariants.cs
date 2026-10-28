using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;

namespace FlushedYo.Tests.StubPlugin;

/// <summary>A valid plugin whose reported name does not match its manifest.</summary>
public sealed class MismatchedNamePlugin : IProviderPlugin
{
    private readonly string _version;

    /// <summary>Creates the plugin.</summary>
    /// <param name="context">The services supplied by the loader.</param>
    public MismatchedNamePlugin(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _version = context.Descriptor.Version;
    }

    /// <inheritdoc />
    public string Name => "someone-else";

    /// <inheritdoc />
    public string Version => _version;

    /// <inheritdoc />
    public Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Transaction>>([]);

    /// <inheritdoc />
    public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Holding>>([]);
}

/// <summary>A plugin whose constructor throws a message that must never reach users.</summary>
public sealed class ThrowingConstructorPlugin : IProviderPlugin
{
    /// <summary>Always throws.</summary>
    /// <param name="context">The services supplied by the loader.</param>
    public ThrowingConstructorPlugin(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        throw new InvalidOperationException("SECRET-IN-MESSAGE");
    }

    /// <inheritdoc />
    public string Name => throw new NotSupportedException();

    /// <inheritdoc />
    public string Version => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>A plugin with only a parameterless constructor, which the loader does not support.</summary>
public sealed class NoContextConstructorPlugin : IProviderPlugin
{
    /// <inheritdoc />
    public string Name => "stub";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Transaction>>([]);

    /// <inheritdoc />
    public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Holding>>([]);
}

/// <summary>A public class that does not implement <see cref="IProviderPlugin"/>.</summary>
public sealed class NotAPlugin
{
    /// <summary>Creates the instance.</summary>
    /// <param name="context">The services supplied by the loader.</param>
    public NotAPlugin(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }
}

/// <summary>An abstract <see cref="IProviderPlugin"/>, which cannot be constructed.</summary>
public abstract class AbstractPlugin : IProviderPlugin
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Version { get; }

    /// <inheritdoc />
    public abstract Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default);
}
