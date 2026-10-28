namespace FlushedYo.Core.Plugin;

/// <summary>
/// A plugin discovered on disk: its validated manifest, configuration, and folder.
/// Descriptors carry no executable code; <see cref="IProviderPlugin"/> implementations arrive in later phases.
/// </summary>
/// <param name="Manifest">Validated <c>plugin.json</c> contents.</param>
/// <param name="Configuration">Validated <c>config.json</c> contents, or <see cref="PluginConfiguration.Default"/>.</param>
/// <param name="DirectoryPath">Absolute path of the plugin folder.</param>
public sealed record PluginDescriptor(
    PluginManifest Manifest,
    PluginConfiguration Configuration,
    string DirectoryPath)
{
    /// <summary>Plugin name from the manifest.</summary>
    public string Name => Manifest.Name;

    /// <summary>Plugin version from the manifest.</summary>
    public string Version => Manifest.Version;

    /// <summary>Capabilities declared in the manifest.</summary>
    public IReadOnlyList<PluginCapability> Capabilities => Manifest.Capabilities;
}
