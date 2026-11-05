using FlushedYo.Core.AgentBridge;

namespace FlushedYo.IntegrationTests.Browser;

[Collection("RealBrowser")]
public sealed class BrowserAttackTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OriginSwapBetweenSnapshotAndEntry_FailsClosedWithoutCredentialSubmission()
    {
        Task? swapped = null;
        TaskCompletionSource? gate = null;
        await using var harness = await SetupEntryAttackAsync(
            "/origin-swap",
            site => $$"""
            <!doctype html><input id="secret" type="password">
            <script>fetch('/gate').then(() => { location.href = 'http://localhost:{{site.Port}}/swapped'; });</script>
            """,
            site =>
            {
                swapped = site.SignalWhenRequested("/swapped");
                gate = site.MapGate("/gate");
                site.MapHtml("/swapped", "<!doctype html><input id=\"secret\" type=\"password\">");
            });
        gate!.SetResult();
        await swapped!.WaitAsync(Token);
        await WaitForSnapshotChangeAsync(harness.Bridge, harness.Expected);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            harness.Bridge.EnterCredentialAsync("synthetic/password", "#secret", harness.Expected, Token));
        Assert.Empty(harness.Site.FormSecrets);
        await AssertFaulted(harness.Bridge);
    }

    [Fact]
    public async Task MidEntryDocumentNavigation_IsFrozenAndFaultsSession()
    {
        await using var harness = await SetupEntryAttackAsync("/freeze", """
            <!doctype html><input id="secret" type="password"
              oninput="location.href='/frozen-attempt'">
            """);
        harness.Site.MapHtml("/frozen-attempt", "<!doctype html>Navigation must not reach the server");

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            harness.Bridge.EnterCredentialAsync("synthetic/password", "#secret", harness.Expected, Token));
        Assert.False(harness.Site.RequestCounts.ContainsKey("/frozen-attempt"));
        await AssertFaulted(harness.Bridge);
    }

    [Fact]
    public async Task TopFrameSwapAfterSnapshot_FailsClosed()
    {
        Task? swapped = null;
        TaskCompletionSource? gate = null;
        await using var harness = await SetupEntryAttackAsync(
            "/frame-swap",
            """
            <!doctype html><input id="secret" type="password">
            <script>fetch('/gate').then(() => { location.href = '/frame-swapped'; });</script>
            """,
            site =>
            {
                swapped = site.SignalWhenRequested("/frame-swapped");
                gate = site.MapGate("/gate");
                site.MapHtml("/frame-swapped", "<!doctype html><input id=\"secret\" type=\"password\">");
            });
        gate!.SetResult();
        await swapped!.WaitAsync(Token);
        await WaitForSnapshotChangeAsync(harness.Bridge, harness.Expected);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            harness.Bridge.EnterCredentialAsync("synthetic/password", "#secret", harness.Expected, Token));
        await AssertFaulted(harness.Bridge);
    }

    [Fact]
    public async Task NewTabInjectionDuringEntry_IsClosedAndFaultsSession()
    {
        await using var harness = await SetupEntryAttackAsync("/popup-attack", """
            <!doctype html><input id="secret" type="password"
              oninput="window.open('/popup', '_blank')">
            """);
        harness.Site.MapHtml("/popup", "<!doctype html>Injected target");

        var exception = await Assert.ThrowsAnyAsync<BridgeException>(() =>
            harness.Bridge.EnterCredentialAsync("synthetic/password", "#secret", harness.Expected, Token));

        Assert.Contains("additional page target", exception.Message, StringComparison.Ordinal);
        await AssertFaulted(harness.Bridge);
    }

    [Fact]
    public async Task TargetFloodDuringEntry_FaultsAndTearsDownWithoutHanging()
    {
        await using var harness = await SetupEntryAttackAsync("/flood", """
            <!doctype html><input id="secret" type="password"
              oninput="for (let i = 0; i < 40; i++) { window.open('/popup', '_blank'); }">
            """);
        harness.Site.MapHtml("/popup", "<!doctype html>Injected target");

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            harness.Bridge.EnterCredentialAsync("synthetic/password", "#secret", harness.Expected, Token));

        await AssertFaulted(harness.Bridge);
    }

    [Fact]
    public async Task FormActionRetargetedToAnotherAllowedOrigin_NeverDeliversTheSecret()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapHtml("/retarget", $$"""
            <!doctype html><title>Retarget</title>
            <form id="login" method="post" action="http://localhost:{{site.Port}}/submit">
              <input id="secret" name="secret" type="password">
            </form>
            """);
        site.MapHtml("/submit", "<!doctype html><title>received</title>");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/retarget"), Token);
        var snapshot = await bridge.GetSecuritySnapshotAsync(Token);

        await Assert.ThrowsAnyAsync<BridgeException>(() =>
            bridge.SubmitCredentialAsync(
                SubmitCredentialArgs.Create("synthetic/password", "#secret", snapshot), Token));

        Assert.False(site.RequestCounts.ContainsKey("/submit"));
        Assert.Empty(site.FormSecrets);
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

    private static async Task WaitForSnapshotChangeAsync(TrustedBrowserBridge bridge, BrowserSecuritySnapshot expected)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var current = await bridge.GetSecuritySnapshotAsync(Token);
            if (current != expected)
            {
                return;
            }

            await Task.Delay(25, Token);
        }

        Assert.Fail("The synthetic navigation did not commit within the bounded wait.");
    }

    private static async Task AssertFaulted(TrustedBrowserBridge bridge)
    {
        await Assert.ThrowsAsync<BridgeOperationException>(() => bridge.GetSecuritySnapshotAsync(Token));
    }

    private sealed class AttackHarness : IAsyncDisposable
    {
        public AttackHarness(SyntheticSite site, TrustedBrowserBridge bridge, BrowserSecuritySnapshot expected)
        {
            Site = site;
            Bridge = bridge;
            Expected = expected;
        }

        public SyntheticSite Site { get; }
        public TrustedBrowserBridge Bridge { get; }
        public BrowserSecuritySnapshot Expected { get; }

        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            await Site.DisposeAsync();
        }
    }

    private static Task<AttackHarness> SetupEntryAttackAsync(
        string path,
        string html,
        Action<SyntheticSite>? extra = null) =>
        SetupEntryAttackAsync(path, _ => html, extra);

    private static async Task<AttackHarness> SetupEntryAttackAsync(
        string path,
        Func<SyntheticSite, string> renderHtml,
        Action<SyntheticSite>? extra = null)
    {
        BrowserTestEnvironment.RequireChromium();
        var site = new SyntheticSite();
        site.MapHtml(path, renderHtml(site));
        extra?.Invoke(site);
        var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url(path), Token);
        var expected = await bridge.GetSecuritySnapshotAsync(Token);
        return new AttackHarness(site, bridge, expected);
    }
}
