using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Cheap, secret-safe checks against the Bitwarden CLI used by live tests to fail fast when the
/// vault cannot serve the fixture item, before paying the MCP server startup cost.
/// </summary>
internal static partial class BitwardenCli
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Why <c>bw get item</c> could not return an item.</summary>
    internal enum ReadFailureKind
    {
        /// <summary>The item does not exist in the vault.</summary>
        NotFound,

        /// <summary>The vault is locked or the configured session is invalid.</summary>
        VaultUnavailable,

        /// <summary>Any other CLI failure.</summary>
        Unknown,
    }

    /// <summary>
    /// Verifies that <paramref name="itemName"/> can be read, failing the current test immediately
    /// when it cannot. Standard output is always discarded because it contains the item's password.
    /// </summary>
    internal static async Task RequireLoginItemAsync(string itemName)
    {
        var item = await RunAsync(["--nointeraction", "get", "item", itemName]);
        if (item.ExitCode == 0 && item.OutputLength > 0)
        {
            return;
        }

        switch (ClassifyReadFailure(item.Error))
        {
            case ReadFailureKind.NotFound:
                Assert.Fail($"Bitwarden item '{itemName}' not found. Create a Login item named '{itemName}' with a password in the vault used by BW_SESSION.");
                break;

            case ReadFailureKind.VaultUnavailable:
                Assert.Fail("Bitwarden vault is locked or BW_SESSION is invalid. Run 'bw unlock --raw' and export BW_SESSION.");
                break;

            default:
                Assert.Fail($"Could not read Bitwarden item '{itemName}' via 'bw' (exit code {item.ExitCode}): {Excerpt(item.Error)}");
                break;
        }
    }

    /// <summary>Classifies the standard error output of a failed <c>bw get item</c> call.</summary>
    internal static ReadFailureKind ClassifyReadFailure(string errorText)
    {
        if (VaultUnavailableRegex().IsMatch(errorText))
        {
            return ReadFailureKind.VaultUnavailable;
        }

        return NotFoundRegex().IsMatch(errorText) ? ReadFailureKind.NotFound : ReadFailureKind.Unknown;
    }

    private static async Task<CliResult> RunAsync(string[] arguments)
    {
        var startInfo = new ProcessStartInfo("bw")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            Assert.Fail("Could not start 'bw'.");
            return new CliResult(-1, 0, string.Empty);
        }

        // A session-less invocation must fail instead of prompting for a master password.
        process.StandardInput.Close();

        // Drain stdout without retaining it so a password-bearing payload cannot deadlock the
        // pipe or leak into test output.
        var outputLengthTask = DiscardAsync(process.StandardOutput);
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"'bw {string.Join(' ', arguments)}' timed out after {Timeout.TotalSeconds:0}s.");
        }

        return new CliResult(process.ExitCode, await outputLengthTask, await errorTask);
    }

    private static async Task<long> DiscardAsync(StreamReader reader)
    {
        long length = 0;
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            length += read;
        }

        return length;
    }

    private static string Excerpt(string text)
    {
        var normalized = string.Join(' ', text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length switch
        {
            0 => "no error output",
            <= 300 => normalized,
            _ => normalized[..300],
        };
    }

    [GeneratedRegex(@"locked|unauthori[sz]ed|session|master password|not logged in", RegexOptions.IgnoreCase)]
    private static partial Regex VaultUnavailableRegex();

    [GeneratedRegex(@"\bnot found\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotFoundRegex();

    private readonly record struct CliResult(int ExitCode, long OutputLength, string Error);
}
