namespace FlushedYo.Core.Plugin;

/// <summary>
/// Validated contents of a plugin's <c>plugin.json</c> manifest (plan §7).
/// </summary>
public sealed record PluginManifest
{
    /// <summary>Plugin name. Lowercase letters, digits, and hyphens only; must match the plugin folder name.</summary>
    public required string Name { get; init; }

    /// <summary>Plugin version. SemVer 2.0, for example <c>1.0.0</c> or <c>1.2.3-beta.1</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Declared capabilities. Empty when the manifest omits them.</summary>
    public IReadOnlyList<PluginCapability> Capabilities { get; init; } = [];

    /// <summary>Names of the MCP servers the plugin requires. Empty when the manifest omits them.</summary>
    public IReadOnlyList<string> McpServers { get; init; } = [];

    /// <summary>Skill names the plugin uses. Each must exist under the plugin's <c>skills/</c> folder.</summary>
    public IReadOnlyList<string> Skills { get; init; } = [];

    /// <summary>Tool names the plugin uses. Each must exist under the plugin's <c>tools/</c> folder.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Relative path (forward slashes) to the plugin's <c>.dll</c> inside the plugin folder, or
    /// <see langword="null"/>. A plugin that declares neither <see cref="Assembly"/> nor <see cref="Type"/>
    /// is descriptor-only and cannot be run.
    /// </summary>
    public string? Assembly { get; init; }

    /// <summary>
    /// Full name of the public class in <see cref="Assembly"/> that implements <see cref="IProviderPlugin"/>,
    /// or <see langword="null"/>. Declared together with <see cref="Assembly"/>.
    /// </summary>
    public string? Type { get; init; }
}
