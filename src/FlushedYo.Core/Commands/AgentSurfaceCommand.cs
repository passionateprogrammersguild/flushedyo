namespace FlushedYo.Core.Commands;

/// <summary>
/// Executes the <c>skills</c> and <c>tools</c> subcommands: list every entry, or read one document.
/// Output follows the shared CLI conventions: <c>--json</c> prints JSON via <see cref="CliJson"/>,
/// otherwise a table or the document text itself.
/// </summary>
public sealed class AgentSurfaceCommand(AgentSkillsService skills)
{
    /// <summary>Lists, or reads, a skill entry.</summary>
    /// <param name="options">Parsed command-line flags; <see cref="CommandLineOptions.Plugin"/> doubles as the read target for <c>skills read</c>.</param>
    /// <param name="readName">The skill name to read, or <see langword="null"/> to list.</param>
    /// <param name="output">Writer for command output (stdout).</param>
    /// <param name="error">Writer for errors (stderr).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code: 0 on success, 1 when the read target is unknown or missing.</returns>
    public async Task<int> ExecuteSkillsAsync(
        CommandLineOptions options, string? readName, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (readName is null)
        {
            var entries = await skills.ListAsync(cancellationToken);
            Write(output, options.Json, CliJson.Serialize(new SkillsResult(entries)), AgentSurfaceFormatter.FormatSkills(entries));
            return 0;
        }

        if (options.Plugin is null)
        {
            error.WriteLine("Usage: flushedyo skills read <plugin> <name>");
            return 1;
        }

        var skill = await skills.ReadAsync(options.Plugin, readName, cancellationToken);
        if (skill is null)
        {
            error.WriteLine($"Unknown skill '{readName}' for plugin '{options.Plugin}'.");
            return 1;
        }

        Write(
            output,
            options.Json,
            CliJson.Serialize(new SkillsResult([], skill.Plugin, skill.Name, skill.Path, skill.Content)),
            skill.Content);
        return 0;
    }

    /// <summary>Lists, or reads, a tool entry.</summary>
    /// <param name="options">Parsed command-line flags; <see cref="CommandLineOptions.Plugin"/> doubles as the read target for <c>tools read</c>.</param>
    /// <param name="readName">The tool name to read, or <see langword="null"/> to list.</param>
    /// <param name="output">Writer for command output (stdout).</param>
    /// <param name="error">Writer for errors (stderr).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code: 0 on success, 1 when the read target is unknown or missing.</returns>
    public async Task<int> ExecuteToolsAsync(
        CommandLineOptions options, string? readName, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (readName is null)
        {
            var entries = await skills.ListToolsAsync(cancellationToken);
            Write(output, options.Json, CliJson.Serialize(new ToolsResult(entries)), AgentSurfaceFormatter.FormatTools(entries));
            return 0;
        }

        if (options.Plugin is null)
        {
            error.WriteLine("Usage: flushedyo tools read <plugin> <name>");
            return 1;
        }

        var tool = await skills.ReadToolAsync(options.Plugin, readName, cancellationToken);
        if (tool is null)
        {
            error.WriteLine($"Unknown tool '{readName}' for plugin '{options.Plugin}'.");
            return 1;
        }

        Write(
            output,
            options.Json,
            CliJson.Serialize(new ToolsResult([], tool.Plugin, tool.Name, tool.Path, tool.Content)),
            tool.Content);
        return 0;
    }

    private static void Write(TextWriter output, bool json, string jsonText, string text) =>
        output.WriteLine(json ? jsonText : text);
}
