using System.Runtime.CompilerServices;
using FlushedYo.Core.Configuration;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Runs the real @bitwarden/mcp-server (downloaded by npx) against a stub 'bw' script on PATH,
/// proving the default tool/argument mapping matches the official server without a real vault.
/// </summary>
[Collection("EnvironmentMutation")]
[Trait("Category", "Network")]
public sealed class NetworkBitwardenTests
{
    [NetworkFact]
    public async Task GetSecret_RealMcpServer_WithStubBw_ResolvesPassword()
    {
        var dir = Directory.CreateTempSubdirectory("flushedyo-stubbw-").FullName;
        var log = Path.Combine(dir, "args.log");
        try
        {
            var script = Path.Combine(dir, "bw");
            await File.WriteAllTextAsync(script,
                "#!/bin/sh\n" +
                $"echo \"$@\" >> '{log}'\n" +
                "case \"$1\" in\n" +
                "  status) echo '{\"status\":\"unlocked\"}' ;;\n" +
                "  get) echo 'stub-secret' ;;\n" +
                "esac\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var path = dir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            var store = StoreFactory.Create(new BitwardenMcpOptions
            {
                Command = "npx",
                Arguments = ["-y", "@bitwarden/mcp-server"],
                ConnectTimeoutSeconds = 120,
                CallTimeoutSeconds = 30,
                EnvironmentVariables = new Dictionary<string, string> { ["PATH"] = path, ["BW_SESSION"] = "stub-session" },
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            var secret = await store.GetSecretAsync("testconnection", cts.Token);

            Assert.Equal("stub-secret", secret);
            var logged = await File.ReadAllTextAsync(log, cts.Token);
            Assert.Contains("get password testconnection", logged);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

internal sealed class NetworkFactAttribute : FactAttribute
{
    public NetworkFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, "npx")));

        if (OperatingSystem.IsWindows())
        {
            Skip = "Network test uses a shell-script stub 'bw' and is not supported on Windows.";
        }
        else if (!onPath)
        {
            Skip = "npx was not found on PATH.";
        }
    }
}
