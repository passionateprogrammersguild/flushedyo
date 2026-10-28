using FlushedYo.Core.Configuration;
using FlushedYo.Core.Plugin;
using Microsoft.Extensions.Options;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Catalog caching and cancellation (spec: plugin_discovery_loader).</summary>
public sealed class PluginCatalogTests
{
    private static PluginCatalog CreateCatalog(string root) =>
        new(new PluginLoader(
            Options.Create(new FlushedYoOptions { PluginDirectory = root }),
            new CapturingLogger<PluginLoader>()));

    [Fact]
    public async Task GetPluginsAsync_ReturnsLoadedPlugins()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity", "2.1.0"));

        var plugins = await CreateCatalog(temp.Root).GetPluginsAsync(TestContext.Current.CancellationToken);

        var plugin = Assert.Single(plugins);
        Assert.Equal("fidelity", plugin.Name);
        Assert.Equal("2.1.0", plugin.Version);
    }

    [Fact]
    public async Task GetPluginsAsync_CachesResultAcrossCalls()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        var catalog = CreateCatalog(temp.Root);

        var first = await catalog.GetPluginsAsync(TestContext.Current.CancellationToken);
        temp.AddPlugin("schwab", TempPluginDirectory.Manifest("schwab"));
        var second = await catalog.GetPluginsAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal("fidelity", Assert.Single(second).Name);
    }

    [Fact]
    public async Task GetPluginsAsync_WithCancelledToken_Throws()
    {
        using var temp = new TempPluginDirectory();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateCatalog(temp.Root).GetPluginsAsync(cts.Token));
    }
}
