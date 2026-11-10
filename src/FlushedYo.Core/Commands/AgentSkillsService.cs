using FlushedYo.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Emits the skill and tool documents plugins declare. This is the contract agent harnesses read:
/// the documents tell the harness (and the model) what the plugin needs — including which MCP
/// servers to start, because the manifest deliberately does not declare them.
/// </summary>
public sealed partial class AgentSkillsService(IPluginCatalog catalog, ILogger<AgentSkillsService> logger)
{
    /// <summary>Lists all declared skills, without reading their content.</summary>
    /// <param name="cancellationToken">Token that cancels the plugin scan.</param>
    /// <returns>Skills sorted by plugin, then name.</returns>
    public async Task<IReadOnlyList<AgentSkill>> ListAsync(CancellationToken cancellationToken = default)
    {
        var plugins = await catalog.GetPluginsAsync(cancellationToken);
        var skills = new List<AgentSkill>();
        foreach (var plugin in plugins)
        {
            foreach (var name in plugin.Manifest.Skills)
            {
                var path = ResolveEntry(plugin, "skills", name);
                if (path is null)
                {
                    LogMissing(logger, plugin.Name, name);
                    continue;
                }

                skills.Add(new AgentSkill(plugin.Name, name, path, string.Empty));
            }
        }

        return skills;
    }

    /// <summary>Reads one skill document.</summary>
    /// <param name="plugin">Plugin name.</param>
    /// <param name="name">Skill name.</param>
    /// <param name="cancellationToken">Token that cancels the plugin scan.</param>
    /// <returns>The skill with its content, or <see langword="null"/> when the plugin or skill is unknown.</returns>
    public async Task<AgentSkill?> ReadAsync(string plugin, string name, CancellationToken cancellationToken = default)
    {
        var plugins = await catalog.GetPluginsAsync(cancellationToken);
        var descriptor = plugins.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, plugin, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !descriptor.Manifest.Skills.Contains(name, StringComparer.Ordinal))
        {
            return null;
        }

        var path = ResolveEntry(descriptor, "skills", name);
        if (path is null)
        {
            LogMissing(logger, descriptor.Name, name);
            return null;
        }

        return new AgentSkill(descriptor.Name, name, path, await File.ReadAllTextAsync(path, cancellationToken));
    }

    /// <summary>Lists all declared tool documents, without reading their content.</summary>
    /// <param name="cancellationToken">Token that cancels the plugin scan.</param>
    /// <returns>Tools sorted by plugin, then name.</returns>
    public async Task<IReadOnlyList<AgentTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var plugins = await catalog.GetPluginsAsync(cancellationToken);
        var tools = new List<AgentTool>();
        foreach (var plugin in plugins)
        {
            foreach (var name in plugin.Manifest.Tools)
            {
                var path = ResolveEntry(plugin, "tools", name);
                if (path is null)
                {
                    LogMissing(logger, plugin.Name, name);
                    continue;
                }

                tools.Add(new AgentTool(plugin.Name, name, path, string.Empty));
            }
        }

        return tools;
    }

    /// <summary>Reads one tool document.</summary>
    /// <param name="plugin">Plugin name.</param>
    /// <param name="name">Tool name.</param>
    /// <param name="cancellationToken">Token that cancels the plugin scan.</param>
    /// <returns>The tool with its content, or <see langword="null"/> when the plugin or tool is unknown.</returns>
    public async Task<AgentTool?> ReadToolAsync(string plugin, string name, CancellationToken cancellationToken = default)
    {
        var plugins = await catalog.GetPluginsAsync(cancellationToken);
        var descriptor = plugins.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, plugin, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !descriptor.Manifest.Tools.Contains(name, StringComparer.Ordinal))
        {
            return null;
        }

        var path = ResolveEntry(descriptor, "tools", name);
        if (path is null)
        {
            LogMissing(logger, descriptor.Name, name);
            return null;
        }

        return new AgentTool(descriptor.Name, name, path, await File.ReadAllTextAsync(path, cancellationToken));
    }

    // Mirrors PluginFileParser's entry resolution: <entry> or <entry>.md inside the subfolder.
    private static string? ResolveEntry(PluginDescriptor plugin, string subfolder, string name)
    {
        var direct = Path.Combine(plugin.DirectoryPath, subfolder, name);
        if (File.Exists(direct))
        {
            return Path.GetFullPath(direct);
        }

        var withExtension = Path.Combine(plugin.DirectoryPath, subfolder, name + ".md");
        if (File.Exists(withExtension))
        {
            return Path.GetFullPath(withExtension);
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skill/tool file for plugin {Plugin} entry {Entry} is missing; skipping.")]
    private static partial void LogMissing(ILogger logger, string plugin, string entry);
}
