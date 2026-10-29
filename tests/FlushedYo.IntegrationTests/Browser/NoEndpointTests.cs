using System.Diagnostics;

namespace FlushedYo.IntegrationTests.Browser;

[Collection("RealBrowser")]
public sealed class NoEndpointTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LiveSession_UsesPipeTransportAndOpensNoBrowserListener()
    {
        var executable = BrowserTestEnvironment.RequireChromium();
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Process inspection uses ps/lsof.");
        await using var site = new SyntheticSite();
        site.MapHtml("/page", "<!doctype html><title>Page</title>");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/page"), Token);

        var commands = await RunAsync("ps", "-axo pid=,command=");
        var browserLines = commands
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(executable, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(browserLines);
        Assert.Contains(browserLines, line => line.Contains("--remote-debugging-pipe", StringComparison.Ordinal));
        Assert.DoesNotContain(browserLines, line => line.Contains("--remote-debugging-port", StringComparison.Ordinal));
        Assert.DoesNotContain(browserLines, line => line.Contains("--remote-debugging-address", StringComparison.Ordinal));

        var pids = browserLines
            .Select(line => line.TrimStart().Split(' ', 2)[0])
            .Where(pid => int.TryParse(pid, out _))
            .ToHashSet(StringComparer.Ordinal);
        var listeners = await RunAsync("lsof", "-nP -iTCP -sTCP:LISTEN -Fp");
        var listenerPids = listeners
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('p'))
            .Select(line => line[1..]);
        Assert.DoesNotContain(listenerPids, pids.Contains);
    }

    private static async Task<string> RunAsync(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var output = await process.StandardOutput.ReadToEndAsync(Token);
        await process.WaitForExitAsync(Token);
        return output;
    }
}
