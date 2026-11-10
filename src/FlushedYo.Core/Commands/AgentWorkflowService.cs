using FlushedYo.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Computes the agent workflow state (tally's <c>workflow</c> command). Read-only: it inspects
/// what is on disk and suggests the next step for an agent. It never loads secrets, opens the
/// ledger, or starts MCP servers.
/// </summary>
public sealed partial class AgentWorkflowService(
    string pluginDirectory,
    string ledgerPath,
    IPluginCatalog catalog,
    ILogger<AgentWorkflowService> logger)
{
    /// <summary>Computes the workflow state.</summary>
    /// <param name="cancellationToken">Token that cancels the plugin scan.</param>
    /// <returns>The workflow state.</returns>
    public async Task<AgentWorkflow> GetAsync(CancellationToken cancellationToken = default)
    {
        var ledgerExists = File.Exists(ledgerPath);
        var plugins = await catalog.GetPluginsAsync(cancellationToken);

        var skillCount = plugins.Sum(static plugin => plugin.Manifest.Skills.Count);
        var toolCount = plugins.Sum(static plugin => plugin.Manifest.Tools.Count);
        var next = ComputeNextStep(ledgerExists, plugins, skillCount);

        LogState(logger, plugins.Count, skillCount, toolCount, next?.Code ?? "ready");
        return new AgentWorkflow(
            pluginDirectory,
            ledgerExists ? ledgerPath : null,
            plugins.Count,
            [.. plugins.Select(static plugin => plugin.Name)],
            skillCount,
            toolCount,
            next);
    }

    private static AgentNextStep? ComputeNextStep(
        bool ledgerExists, IReadOnlyList<PluginDescriptor> plugins, int skillCount)
    {
        if (plugins.Count == 0)
        {
            return new AgentNextStep(
                "no-plugins",
                "No plugins are installed; add a plugin folder under the plugin directory.",
                "flushedyo plugins list");
        }

        if (skillCount == 0)
        {
            return new AgentNextStep(
                "no-skills",
                "The installed plugins declare no skills; add skill documents under the plugin's skills/ folder.",
                "flushedyo skills list");
        }

        if (!ledgerExists)
        {
            return new AgentNextStep(
                "no-ledger",
                "Run download once to create the ledger and import transactions.",
                "flushedyo download");
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent workflow: plugins {Plugins}, skills {Skills}, tools {Tools}, next {Next}")]
    private static partial void LogState(
        ILogger logger, int plugins, int skills, int tools, string next);
}
