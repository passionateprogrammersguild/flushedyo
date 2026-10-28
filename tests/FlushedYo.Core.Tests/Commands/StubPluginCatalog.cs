using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>An <see cref="IPluginCatalog"/> that returns a fixed list.</summary>
internal sealed class StubPluginCatalog(params PluginDescriptor[] plugins) : IPluginCatalog
{
    public Task<IReadOnlyList<PluginDescriptor>> GetPluginsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginDescriptor>>(plugins);
}

/// <summary>Builders for command tests.</summary>
internal static class CommandTestData
{
    public static PluginDescriptor Descriptor(string name, string? usernameKey = null, string? passwordKey = null) =>
        new(
            new PluginManifest { Name = name, Version = "1.0.0" },
            new PluginConfiguration { CredentialKeys = new PluginCredentialKeys { Username = usernameKey, Password = passwordKey } },
            "/plugins/" + name);
}
