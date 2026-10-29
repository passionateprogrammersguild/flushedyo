using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class ManualMfaPauseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WaitProceedsOnlyWhenExpectedPostLoginOriginAppears()
    {
        var time = new ManualTimeProvider();
        using var harness = new BridgeTestHarness(timeProvider: time);
        var postLogin = TrustedBrowserBridgeCredentialTests.Snapshot(origin: "https://other.example:443");
        harness.Driver.SnapshotQueue.Enqueue(harness.Driver.CurrentSnapshot);
        harness.Driver.SnapshotQueue.Enqueue(postLogin);

        var wait = harness.Bridge.WaitForManualMfaAsync(
            new Uri("https://other.example/after-login"),
            TimeSpan.FromSeconds(1),
            Token);
        await time.TimerCreated.Task.WaitAsync(Token);
        time.Advance(TimeSpan.FromMilliseconds(50));

        var result = await wait.WaitAsync(Token);
        Assert.Equal(postLogin, result);
        Assert.False(harness.Driver.Closed);
    }

    [Fact]
    public async Task TimeoutIsBoundedAndValueFree()
    {
        var time = new ManualTimeProvider();
        using var harness = new BridgeTestHarness(timeProvider: time);

        var wait = harness.Bridge.WaitForManualMfaAsync(
            new Uri("https://other.example/private/path?secret=value"),
            TimeSpan.FromMilliseconds(100),
            Token);
        await time.TimerCreated.Task.WaitAsync(Token);
        time.Advance(TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<BridgeMfaException>(() => wait.WaitAsync(Token));
        Assert.DoesNotContain("private/path", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret=value", exception.Message, StringComparison.Ordinal);
        Assert.True(harness.Driver.Closed);
    }

    [Fact]
    public async Task DisallowedExpectedOrigin_FailsClosed()
    {
        using var harness = new BridgeTestHarness();

        await Assert.ThrowsAsync<BridgePolicyException>(() => harness.Bridge.WaitForManualMfaAsync(
            new Uri("https://outside.example/account"),
            TimeSpan.FromSeconds(1),
            Token));
        Assert.True(harness.Driver.Closed);
    }

    [Fact]
    public async Task RedirectToDisallowedOriginDuringWait_FailsClosed()
    {
        using var harness = new BridgeTestHarness();
        harness.Driver.SnapshotHook = (_, _) =>
            Task.FromException(new BridgePolicyException("The committed browser origin is not allowed."));

        await Assert.ThrowsAsync<BridgePolicyException>(() => harness.Bridge.WaitForManualMfaAsync(
            new Uri("https://other.example"),
            TimeSpan.FromSeconds(1),
            Token));
        Assert.True(harness.Driver.Closed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task InvalidTimeout_IsCallerErrorAndDoesNotCloseSession(int seconds)
    {
        using var harness = new BridgeTestHarness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Bridge.WaitForManualMfaAsync(
            new Uri("https://other.example"),
            TimeSpan.FromSeconds(seconds),
            Token));
        Assert.False(harness.Driver.Closed);
    }

    [Fact]
    public void ProviderSurface_HasNoMfaCodeInputOrAutomaticMfaOperation()
    {
        var methods = typeof(TrustedBrowserBridge).GetMethods(System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(methods, method =>
            method.Name.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("AutomaticMfa", StringComparison.OrdinalIgnoreCase));
        Assert.Single(methods, method => method.Name == nameof(TrustedBrowserBridge.WaitForManualMfaAsync));
    }
}
