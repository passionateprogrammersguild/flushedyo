using Microsoft.Extensions.DependencyInjection;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Routes the <c>skills</c> and <c>tools</c> subcommands: build a request from the raw tokens, then
/// await the one matching execute method. Parsing is pure; all I/O goes through the supplied writers.
/// </summary>
public static class AgentSurfaceRouter
{
    /// <summary>The tokens after the command name, plus the command itself (<c>skills</c> or <c>tools</c>).</summary>
    public sealed record Input(string[] SubArgs, string Command);

    /// <summary>Command output, error output, and the shared usage printer.</summary>
    public sealed record Writers(TextWriter Output, TextWriter Error, Func<int> PrintUsage);

    private sealed record Request(CommandLineOptions Options, string? ReadName);

    private sealed record ParseResult(Request? Request, string? Error);

    /// <summary>Runs one <c>skills</c>/<c>tools</c> invocation and returns its exit code.</summary>
    public static async Task<int> RunAsync(Input input, IServiceProvider services, Writers writers, CancellationToken token)
    {
        var parsed = TryBuildRequest(input);
        if (parsed.Request is not { } request)
        {
            return Fail(parsed.Error, writers);
        }

        var surface = services.GetRequiredService<AgentSurfaceCommand>();
        return input.Command.Equals("skills", StringComparison.OrdinalIgnoreCase)
            ? await surface.ExecuteSkillsAsync(request.Options, request.ReadName, writers.Output, writers.Error, token)
            : await surface.ExecuteToolsAsync(request.Options, request.ReadName, writers.Output, writers.Error, token);
    }

    private static int Fail(string? error, Writers writers)
    {
        writers.Error.WriteLine(error);
        return writers.PrintUsage();
    }

    private static ParseResult TryBuildRequest(Input input)
    {
        var subArgs = input.SubArgs;
        if (subArgs.Length == 0 || !IsKnownSubcommand(subArgs[0]))
        {
            return new ParseResult(null, $"Usage: flushedyo {input.Command} list | read <plugin> <name> [--json]");
        }

        var json = subArgs.Contains("--json", StringComparer.OrdinalIgnoreCase);
        var rest = subArgs.Where(static arg => !arg.Equals("--json", StringComparison.OrdinalIgnoreCase)).ToArray();
        var arguments = new Subcommand(rest, input.Command, json);
        return rest[0] == "list" ? TryBuildListRequest(arguments) : TryBuildReadRequest(arguments);
    }

    private static bool IsKnownSubcommand(string token) => token is "list" or "read";

    private sealed record Subcommand(string[] Rest, string Command, bool Json);

    private static ParseResult TryBuildListRequest(Subcommand subcommand)
    {
        if (subcommand.Rest.Length > 1)
        {
            return new ParseResult(null, $"Unexpected arguments for flushedyo {subcommand.Command} list.");
        }

        return new ParseResult(new Request(new CommandLineOptions(null, subcommand.Json), null), null);
    }

    private static ParseResult TryBuildReadRequest(Subcommand subcommand)
    {
        if (subcommand.Rest.Length < 3)
        {
            return new ParseResult(null, $"Usage: flushedyo {subcommand.Command} read <plugin> <name> [--json]");
        }

        var options = new CommandLineOptions(subcommand.Rest[1], subcommand.Json);
        return new ParseResult(new Request(options, subcommand.Rest[2]), null);
    }
}
