using FlushedYo.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Commands;

/// <summary>One skill entry a plugin exposes to agent harnesses.</summary>
/// <param name="Plugin">Plugin name that owns the skill.</param>
/// <param name="Name">Skill name from the manifest.</param>
/// <param name="Path">Absolute path of the skill document (<c>skills/&lt;name&gt;</c> or <c>skills/&lt;name&gt;.md</c>).</param>
/// <param name="Content">Full text of the skill document.</param>
public sealed record AgentSkill(string Plugin, string Name, string Path, string Content);

/// <summary>One tool entry a plugin exposes to agent harnesses.</summary>
/// <param name="Plugin">Plugin name that owns the tool.</param>
/// <param name="Name">Tool name from the manifest.</param>
/// <param name="Path">Absolute path of the tool document (<c>tools/&lt;name&gt;</c> or <c>tools/&lt;name&gt;.md</c>).</param>
/// <param name="Content">Full text of the tool document.</param>
public sealed record AgentTool(string Plugin, string Name, string Path, string Content);
