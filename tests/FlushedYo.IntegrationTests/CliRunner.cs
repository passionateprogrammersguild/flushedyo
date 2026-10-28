using System.Diagnostics;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Launches the built CLI as a child process with the fake MCP server configured through
/// environment variables, capturing its exit code and output.
/// </summary>
internal static class CliRunner
{
    internal const string Secret = "fake-secret-value";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    internal static Dictionary<string, string> FakeEnv(string mode, bool withSession = true)
    {
        var env = new Dictionary<string, string>
        {
            ["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__COMMAND"] = "dotnet",
            ["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__ARGUMENTS__0"] = HelperPaths.GetFakeServerDll(),
            ["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__ARGUMENTS__1"] = mode,
        };
        if (withSession)
        {
            env["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__ENVIRONMENTVARIABLES__BW_SESSION"] = "dummy";
        }

        return env;
    }

    internal static async Task<CliResult> RunAsync(
        string[] args, Dictionary<string, string>? env = null, bool removeBwSession = true)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(HelperPaths.GetCliDll());
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (removeBwSession)
        {
            startInfo.EnvironmentVariables.Remove("BW_SESSION");
        }

        if (env != null)
        {
            foreach (var (key, value) in env)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new CliResult(process.ExitCode, await stdoutTask, await stderrTask);
    }
}

internal readonly record struct CliResult(int ExitCode, string StdOut, string StdErr);
