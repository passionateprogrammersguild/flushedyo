namespace FlushedYo.Core.Commands;

/// <summary>
/// Parses the flags that follow a command name. Pure: no I/O.
/// </summary>
public static class CommandLineParser
{
    private const string PluginFlag = "--plugin";
    private const string JsonFlag = "--json";

    /// <summary>Parses <c>--plugin &lt;name&gt;</c> and <c>--json</c>.</summary>
    /// <param name="arguments">The tokens after the command name.</param>
    /// <param name="options">The parsed options; never <see langword="null"/> (defaults on failure).</param>
    /// <param name="error">A usage message when parsing fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every token was understood.</returns>
    /// <remarks>
    /// Flags are case-sensitive, may appear in any order, and may each appear at most once.
    /// </remarks>
    public static bool TryParse(IReadOnlyList<string> arguments, out CommandLineOptions options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? plugin = null;
        var pluginSeen = false;
        var json = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];

            if (token == PluginFlag)
            {
                if (pluginSeen)
                {
                    return Fail($"Option '{PluginFlag}' was specified more than once.", out options, out error);
                }

                pluginSeen = true;
                var value = index + 1 < arguments.Count ? arguments[index + 1] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
                {
                    return Fail($"Option '{PluginFlag}' requires a plugin name.", out options, out error);
                }

                plugin = value.Trim();
                index++;
            }
            else if (token == JsonFlag)
            {
                if (json)
                {
                    return Fail($"Option '{JsonFlag}' was specified more than once.", out options, out error);
                }

                json = true;
            }
            else if (token.StartsWith("--", StringComparison.Ordinal))
            {
                return Fail($"Unknown option '{token}'.", out options, out error);
            }
            else
            {
                return Fail($"Unexpected argument '{token}'.", out options, out error);
            }
        }

        options = new CommandLineOptions(plugin, json);
        error = null;
        return true;
    }

    private static bool Fail(string message, out CommandLineOptions options, out string? error)
    {
        options = new CommandLineOptions(null, false);
        error = message;
        return false;
    }
}
