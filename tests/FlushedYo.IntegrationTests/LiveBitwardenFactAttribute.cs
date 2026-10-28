using System.Runtime.CompilerServices;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Marks a test that needs the real Bitwarden MCP server. Skipped when node, npx or bw
/// are missing from PATH; fails (by design) when BW_SESSION is unset.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveBitwardenFactAttribute : FactAttribute
{
    public LiveBitwardenFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        var missing = new[] { "node", "npx", "bw" }.Where(tool => !ExistsOnPath(tool)).ToList();
        if (missing.Count > 0)
        {
            Skip = $"Required tool(s) not found on PATH: {string.Join(", ", missing)}.";
        }
    }

    internal static void RequireSession()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BW_SESSION")))
        {
            Assert.Fail("BW_SESSION is not set. Run 'bw unlock --raw' and export BW_SESSION to run live Bitwarden tests.");
        }
    }

    private static bool ExistsOnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] names = OperatingSystem.IsWindows() ? [tool + ".exe", tool + ".cmd", tool + ".bat"] : [tool];
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => names.Any(name => File.Exists(Path.Combine(dir, name))));
    }
}
