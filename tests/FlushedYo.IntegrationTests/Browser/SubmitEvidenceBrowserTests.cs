using FlushedYo.Core.AgentBridge;

namespace FlushedYo.IntegrationTests.Browser;

[Collection("RealBrowser")]
public sealed class SubmitEvidenceBrowserTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PageThatSwallowsTheSubmitKeypress_IsNotReportedAsSubmitted()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapHtml("/swallow", """
            <!doctype html><title>Swallow</title>
            <form id="login" method="post" action="/submit" onsubmit="return false;">
              <input id="secret" name="secret" type="password" onkeydown="if (event.key === 'Enter') { event.preventDefault(); }">
            </form>
            """);
        site.MapHtml("/submit", "<!doctype html><title>done</title>");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/swallow"), Token);
        var snapshot = await bridge.GetSecuritySnapshotAsync(Token);

        await Assert.ThrowsAsync<BridgeOperationException>(() =>
            bridge.SubmitCredentialAsync("synthetic/password", "#secret", snapshot, Token));

        Assert.False(site.RequestCounts.ContainsKey("/submit"));
        Assert.Empty(site.FormSecrets);
    }

    [Fact]
    public async Task PlaywrightDebugLogging_IsRejectedBeforeAnyBrowserStarts()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapHtml("/page", "<!doctype html><title>Page</title>");
        var previous = Environment.GetEnvironmentVariable("DEBUG");
        try
        {
            Environment.SetEnvironmentVariable("DEBUG", "pw:api");
            await using var bridge = site.CreateBridge();

            var exception = await Assert.ThrowsAsync<BridgeOperationException>(() =>
                bridge.NavigateAsync(site.Url("/page"), Token));

            Assert.DoesNotContain(BrowserTestEnvironment.SecretValue, exception.Message, StringComparison.Ordinal);
            Assert.False(site.RequestCounts.ContainsKey("/page"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEBUG", previous);
        }
    }
}
