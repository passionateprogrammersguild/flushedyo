using FlushedYo.Core.AgentBridge;
using Microsoft.Playwright;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class PlaywrightLaunchHardeningTests
{
    [Fact]
    public void LaunchOptions_KeepSandboxAndOpenNoDebugEndpoint()
    {
        var launch = PlaywrightBrowserSessionDriver.BuildLaunchOptions(new AgentBridgeOptions());

        Assert.True(launch.ChromiumSandbox);
        Assert.NotNull(launch.Args);
        Assert.DoesNotContain(launch.Args, arg => arg.Contains("no-sandbox", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(launch.Args, arg => arg.Contains("remote-debugging", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(launch.Args, arg => arg.Contains("disable-web-security", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(launch.Args, arg => arg.Contains("user-data-dir", StringComparison.OrdinalIgnoreCase));
        Assert.Null(launch.DownloadsPath);
        Assert.Null(launch.TracesDir);
        Assert.Null(launch.Proxy);
    }

    [Fact]
    public void BrowserEnvironment_NeverCarriesSessionTokensOrPlaywrightDebugVariables()
    {
        var environment = PlaywrightBrowserSessionDriver.CreateBrowserEnvironment();

        Assert.DoesNotContain("BW_SESSION", environment.Keys);
        Assert.DoesNotContain("DEBUG", environment.Keys);
        Assert.DoesNotContain("PWDEBUG", environment.Keys);
        Assert.All(environment.Keys, key => Assert.DoesNotContain("SECRET", key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContextOptions_BlockDownloadsServiceWorkersAndAllArtifactsOrStoredState()
    {
        var context = PlaywrightBrowserSessionDriver.BuildContextOptions();

        Assert.False(context.AcceptDownloads);
        Assert.Equal(ServiceWorkerPolicy.Block, context.ServiceWorkers);
        Assert.Null(context.RecordHarPath);
        Assert.Null(context.RecordHarContent);
        Assert.Null(context.RecordVideoDir);
        Assert.Null(context.StorageState);
        Assert.Null(context.StorageStatePath);
        Assert.Null(context.Permissions);
        Assert.Null(context.ClientCertificates);
    }

    /// <summary>
    /// Downloads are not enabled globally on the context: the per-operation CDP window is the only
    /// place download behavior is flipped, and it reverts to deny on every exit path.
    /// </summary>
    [Fact]
    public void Downloads_AreNotEnabledGlobally_OnlyPerOperationWindow()
    {
        var driver = new PlaywrightBrowserSessionDriver(
            new ProviderBrowserPolicy(Array.Empty<string>()),
            new AgentBridgeOptions(),
            TimeProvider.System);

        try
        {
            var downloadCapture = typeof(PlaywrightBrowserSessionDriver)
                .GetField("_downloadCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(driver);
            var browserCdp = typeof(PlaywrightBrowserSessionDriver)
                .GetField("_browserCdp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(driver);

            Assert.Null(downloadCapture);
            Assert.Null(browserCdp);
        }
        finally
        {
            driver.Dispose();
        }
    }
}
