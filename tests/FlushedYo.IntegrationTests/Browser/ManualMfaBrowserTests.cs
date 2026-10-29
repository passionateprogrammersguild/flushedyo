using FlushedYo.Core.AgentBridge;

namespace FlushedYo.IntegrationTests.Browser;

[Collection("RealBrowser")]
public sealed class ManualMfaBrowserTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WaitsUntilTheUserCompletesTheChallengeThenReturnsStructuredSnapshot()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        var userCompletesChallenge = site.MapGate("/user-done");
        site.MapHtml("/mfa", $$"""
            <!doctype html><title>Challenge</title><p>Complete the challenge in the browser.</p>
            <script>fetch('/user-done').then(() => { location.href = 'http://localhost:{{site.Port}}/home'; });</script>
            """);
        site.MapHtml("/home", "<!doctype html><title>Home</title>");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/mfa"), Token);

        var waiting = bridge.WaitForManualMfaAsync(site.LocalhostUrl("/home"), TimeSpan.FromSeconds(3), Token);
        Assert.False(waiting.IsCompleted);
        userCompletesChallenge.SetResult();
        var snapshot = await waiting;

        Assert.Equal($"http://localhost:{site.Port}", snapshot.Origin);
    }

    [Fact]
    public async Task TimesOutWithValueFreeErrorWhenTheChallengeIsNeverCompleted()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        site.MapHtml("/mfa", "<!doctype html><title>Challenge</title>");
        await using var bridge = site.CreateBridge();
        await bridge.NavigateAsync(site.Url("/mfa"), Token);

        var exception = await Assert.ThrowsAsync<BridgeMfaException>(() =>
            bridge.WaitForManualMfaAsync(site.LocalhostUrl("/home"), TimeSpan.FromSeconds(1), Token));

        Assert.DoesNotContain(site.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Empty(site.FormSecrets);
    }
}
