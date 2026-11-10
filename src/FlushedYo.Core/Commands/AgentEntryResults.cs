namespace FlushedYo.Core.Commands;

/// <summary>
/// A resolved skill entry: a list row when <see cref="Content"/> is empty, the full document when
/// it is not.
/// </summary>
/// <param name="Entries">All known skill entries when listing; empty after a read.</param>
/// <param name="Plugin">Plugin name of the read entry.</param>
/// <param name="Name">Skill name of the read entry.</param>
/// <param name="Path">Path of the read entry.</param>
/// <param name="Content">Full document text of the read entry.</param>
public sealed record SkillsResult(
    IReadOnlyList<AgentSkill> Entries,
    string? Plugin = null,
    string? Name = null,
    string? Path = null,
    string? Content = null);

/// <summary>
/// A resolved tool entry: a list row when <see cref="Content"/> is empty, the full document when
/// it is not.
/// </summary>
/// <param name="Entries">All known tool entries when listing; empty after a read.</param>
/// <param name="Plugin">Plugin name of the read entry.</param>
/// <param name="Name">Tool name of the read entry.</param>
/// <param name="Path">Path of the read entry.</param>
/// <param name="Content">Full document text of the read entry.</param>
public sealed record ToolsResult(
    IReadOnlyList<AgentTool> Entries,
    string? Plugin = null,
    string? Name = null,
    string? Path = null,
    string? Content = null);
