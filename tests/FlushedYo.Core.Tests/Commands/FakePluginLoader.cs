using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>An <see cref="IPluginAssemblyLoader"/> that returns in-process fake plugins (or throws) by plugin name.</summary>
internal sealed class FakePluginLoader : IPluginAssemblyLoader
{
    private readonly Dictionary<string, Func<LoadedPlugin>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public int LoadCalls { get; private set; }

    public List<string> LoadedNames { get; } = [];

    public FakePluginLoader Add(FakePlugin plugin)
    {
        _factories[plugin.Name] = () => new LoadedPlugin(plugin, null);
        return this;
    }

    public FakePluginLoader AddFailure(string name, Exception exception)
    {
        _factories[name] = () => throw exception;
        return this;
    }

    public LoadedPlugin Load(PluginDescriptor descriptor)
    {
        LoadCalls++;
        LoadedNames.Add(descriptor.Name);
        return _factories.TryGetValue(descriptor.Name, out var factory)
            ? factory()
            : throw new InvalidOperationException($"No fake registered for '{descriptor.Name}'.");
    }
}

/// <summary>
/// A fake plugin. <see cref="DownloadHoldingsAsync"/> throws because <c>download</c> must never call it.
/// </summary>
internal sealed class FakePlugin(
    string name,
    Func<CancellationToken, Task<IEnumerable<Transaction>>> download,
    string version = "1.0.0") : IProviderPlugin, IAsyncDisposable
{
    public int DisposeCount { get; private set; }

    public string Name => name;

    public string Version => version;

    public static FakePlugin Returning(string name, IEnumerable<Transaction> transactions, string version = "1.0.0") =>
        new(name, _ => Task.FromResult(transactions), version);

    public static FakePlugin Throwing(string name, Exception exception) =>
        new(name, _ => throw exception);

    public Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default) =>
        download(cancellationToken);

    public Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("holdings must not be called");

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Descriptor builders for download tests.</summary>
internal static class DownloadTestData
{
    public static PluginDescriptor Runnable(string name, string version = "1.0.0") =>
        new(
            new PluginManifest
            {
                Name = name,
                Version = version,
                Capabilities = [PluginCapability.DownloadTransactions],
                Assembly = "plugin.dll",
                Type = "Company.Plugin",
            },
            PluginConfiguration.Default,
            "/plugins/" + name);

    public static PluginDescriptor DescriptorOnly(string name) =>
        new(
            new PluginManifest { Name = name, Version = "1.0.0", Capabilities = [PluginCapability.DownloadTransactions] },
            PluginConfiguration.Default,
            "/plugins/" + name);

    public static PluginDescriptor WithoutCapability(string name) =>
        new(
            new PluginManifest { Name = name, Version = "1.0.0", Assembly = "plugin.dll", Type = "Company.Plugin" },
            PluginConfiguration.Default,
            "/plugins/" + name);
}
