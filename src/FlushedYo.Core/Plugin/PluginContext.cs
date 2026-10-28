using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// Services handed to a plugin's constructor when it is loaded.
/// </summary>
/// <param name="Descriptor">
/// The plugin's discovered manifest, <c>config.json</c> values (such as credential key names), and folder.
/// </param>
/// <param name="SecretStore">
/// Resolves credentials by key name. Plugins must never log or persist the values it returns.
/// </param>
/// <param name="Logger">A logger scoped to the plugin name.</param>
public sealed record PluginContext(PluginDescriptor Descriptor, ISecretStore SecretStore, ILogger Logger);
