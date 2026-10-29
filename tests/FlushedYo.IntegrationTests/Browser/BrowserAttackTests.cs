using FlushedYo.Core.AgentBridge;

namespace FlushedYo.IntegrationTests.Browser;

public sealed class BrowserAttackTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OriginSwapBetweenSnapshotAndEntry_FailsClosedWithoutCredentialSubmission()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        var swapped = site.SignalWhenRequested("/swapped");
        site.MapHtml("/origin-swap", $$"""
            <!doctype html><input id="secret" type="password">
            <script>setTimeout(() => location.href = 'http://localhost:{{site.Port}}/swapped', 50);</script>
            """);
        site.MapHtml("/swapped", "<!doctype html><input id=\"secret\" type=\"password\">");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/origin-swap"), Token);
        var expected = await bridge.GetSecuritySnapshotAsync(Token);
        await swapped.WaitAsync(Token);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            bridge.EnterCredentialAsync("synthetic/password", "#secret", expected, Token));
        Assert.Empty(site.FormSecrets);
        await AssertFaulted(bridge);
    }

    [Fact]
    public async Task MidEntryDocumentNavigation_IsFrozenAndFaultsSession()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapHtml("/freeze", """
            <!doctype html><input id="secret" type="password"
              oninput="location.href='/frozen-attempt'">
            """);
        site.MapHtml("/frozen-attempt", "<!doctype html>Navigation must not reach the server");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/freeze"), Token);
        var expected = await bridge.GetSecuritySnapshotAsync(Token);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            bridge.EnterCredentialAsync("synthetic/password", "#secret", expected, Token));
        Assert.False(site.RequestCounts.ContainsKey("/frozen-attempt"));
        await AssertFaulted(bridge);
    }

    [Fact]
    public async Task TopFrameSwapAfterSnapshot_FailsClosed()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        var swapped = site.SignalWhenRequested("/frame-swapped");
        site.MapHtml("/frame-swap", """
            <!doctype html><input id="secret" type="password">
            <script>setTimeout(() => location.href = '/frame-swapped', 50);</script>
            """);
        site.MapHtml("/frame-swapped", "<!doctype html><input id=\"secret\" type=\"password\">");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/frame-swap"), Token);
        var expected = await bridge.GetSecuritySnapshotAsync(Token);
        await swapped.WaitAsync(Token);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            bridge.EnterCredentialAsync("synthetic/password", "#secret", expected, Token));
        await AssertFaulted(bridge);
    }

    [Fact]
    public async Task NewTabInjectionDuringEntry_IsClosedAndFaultsSession()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapHtml("/popup-attack", """
            <!doctype html><input id="secret" type="password"
              oninput="window.open('/popup', '_blank')">
            """);
        site.MapHtml("/popup", "<!doctype html>Injected target");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/popup-attack"), Token);
        var expected = await bridge.GetSecuritySnapshotAsync(Token);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            bridge.EnterCredentialAsync("synthetic/password", "#secret", expected, Token));
        await AssertFaulted(bridge);
    }

    [Fact]
    public async Task RedirectToDisallowedOrigin_IsAbortedAndFaultsSession()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapRedirect("/redirect", new Uri("https://outside.example/landing"));
        await using var bridge = site.CreateBridge();

        await Assert.ThrowsAnyAsync<BridgeException>(() => bridge.NavigateAsync(site.Url("/redirect"), Token));
        await AssertFaulted(bridge);
    }

    private static async Task AssertFaulted(TrustedBrowserBridge bridge)
    {
        await Assert.ThrowsAsync<BridgeOperationException>(() => bridge.GetSecuritySnapshotAsync(Token));
    }
}
