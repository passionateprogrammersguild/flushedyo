using System.Text.Json;
using System.Text.Json.Serialization;
using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Commands;

/// <summary>Renders agent-surface commands as text tables or agent-friendly JSON.</summary>
public static class AgentSurfaceFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Renders the workflow state as a human-readable block.</summary>
    public static string FormatWorkflow(AgentWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var lines = new List<string>
        {
            $"Plugin directory: {workflow.PluginDirectory ?? "-"}",
            $"Ledger: {workflow.LedgerPath ?? "-"}",
            $"Plugins: {workflow.Plugins}{(workflow.PluginNames.Count > 0 ? $" ({string.Join(", ", workflow.PluginNames)})" : string.Empty)}",
            $"Skills: {workflow.SkillCount}",
            $"Tools: {workflow.ToolCount}",
        };

        if (workflow.Next is { } next)
        {
            lines.Add(string.Empty);
            lines.Add("Next step:");
            lines.Add($"  {next.Command}");
            lines.Add($"  {next.Message}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>Renders the skill list as a table: PLUGIN, NAME, PATH.</summary>
    public static string FormatSkills(IReadOnlyList<AgentSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        if (skills.Count == 0)
        {
            return "No skills declared.";
        }

        var rows = skills
            .Select(static skill => (IReadOnlyList<string>)[skill.Plugin, skill.Name, skill.Path])
            .ToList();
        return TextTable.Render(["PLUGIN", "NAME", "PATH"], rows);
    }

    /// <summary>Renders the tool list as a table: PLUGIN, NAME, PATH.</summary>
    public static string FormatTools(IReadOnlyList<AgentTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.Count == 0)
        {
            return "No tools declared.";
        }

        var rows = tools
            .Select(static tool => (IReadOnlyList<string>)[tool.Plugin, tool.Name, tool.Path])
            .ToList();
        return TextTable.Render(["PLUGIN", "NAME", "PATH"], rows);
    }

    /// <summary>Renders the agent-surface manifest: skills and tools per plugin, with commands for agents to run.</summary>
    public static string FormatManifest(IReadOnlyList<PluginDescriptor> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var surface = new
        {
            plugins = plugins.Select(plugin => new
            {
                name = plugin.Name,
                version = plugin.Version,
                capabilities = plugin.Capabilities.Select(PluginCapabilityNames.ToManifestName),
                skills = plugin.Manifest.Skills,
                tools = plugin.Manifest.Tools,
            }),
            commands = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["listPlugins"] = "flushedyo plugins list",
                ["listSkills"] = "flushedyo skills list",
                ["readSkill"] = "flushedyo skills read <plugin> <name>",
                ["listTools"] = "flushedyo tools list",
                ["readTool"] = "flushedyo tools read <plugin> <name>",
                ["workflow"] = "flushedyo workflow",
            },
        };

        return JsonSerializer.Serialize(surface, JsonOptions);
    }
}
