using System.Text.Json.Serialization;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Machine-readable state of the whole agent surface, computed from what is on disk —
/// never from secrets, ledger contents, or MCP servers.
/// </summary>
/// <param name="PluginDirectory">Resolved plugin directory, or <see langword="null"/> when it does not exist.</param>
/// <param name="LedgerPath">Resolved ledger path, or <see langword="null"/> when the file is absent.</param>
/// <param name="Plugins">Number of discovered plugins.</param>
/// <param name="PluginNames">Discovered plugin names, sorted.</param>
/// <param name="SkillCount">Total number of skill documents across all plugins.</param>
/// <param name="ToolCount">Total number of tool documents across all plugins.</param>
/// <param name="Next">The single suggested next step, or <see langword="null"/> when the setup is complete.</param>
public sealed record AgentWorkflow(
    string? PluginDirectory,
    string? LedgerPath,
    int Plugins,
    IReadOnlyList<string> PluginNames,
    int SkillCount,
    int ToolCount,
    AgentNextStep? Next);

/// <summary>The suggested next step for an agent working with flushedyo.</summary>
public sealed record AgentNextStep(
    /// <summary>Stable machine code, for example <c>no-plugins</c> or <c>ready</c>.</summary>
    [property: JsonPropertyName("code")] string Code,
    /// <summary>One-value-free sentence describing what to do next.</summary>
    [property: JsonPropertyName("message")] string Message,
    /// <summary>The exact command an agent should run next.</summary>
    [property: JsonPropertyName("command")] string Command);
