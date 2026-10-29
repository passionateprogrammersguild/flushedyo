using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class BridgeSecretFreeOutputTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuccessPath_ReturnsNoSecretAndLogsNoSecret()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;

        await harness.Bridge.EnterCredentialAsync("website/password", "#password", expected, Token);
        var snapshot = await harness.Bridge.GetSecuritySnapshotAsync(Token);

        AssertSecretFree(harness, snapshot.ToString());
        Assert.Equal(BridgeTestHarness.SecretValue, harness.Driver.SecretSeen);
    }

    [Fact]
    public async Task SecretStoreFailure_CannotLeakSecretThroughExceptionOrLogs()
    {
        using var harness = new BridgeTestHarness();
        harness.Secrets.Exception = new InvalidOperationException(BridgeTestHarness.SecretValue);

        var exception = await Assert.ThrowsAsync<BridgeOperationException>(() =>
            harness.Bridge.EnterCredentialAsync(
                "website/password",
                "#password",
                harness.Driver.CurrentSnapshot,
                Token));

        AssertSecretFree(harness, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task BrowserFailureAfterInput_CannotLeakSecretThroughExceptionOrLogs()
    {
        using var harness = new BridgeTestHarness();
        harness.Driver.StageHook = (stage, _) => stage == "fill"
            ? Task.FromException(new InvalidOperationException(BridgeTestHarness.SecretValue))
            : Task.CompletedTask;

        var exception = await Assert.ThrowsAsync<BridgeOperationException>(() =>
            harness.Bridge.SubmitCredentialAsync(
                "website/password",
                "#password",
                harness.Driver.CurrentSnapshot,
                Token));

        AssertSecretFree(harness, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task MissingCredentialFailure_ContainsNeitherSecretNorKeyValue()
    {
        using var harness = new BridgeTestHarness(secretValue: null);

        var exception = await Assert.ThrowsAsync<BridgeOperationException>(() =>
            harness.Bridge.EnterCredentialAsync(
                "website/password",
                "#password",
                harness.Driver.CurrentSnapshot,
                Token));

        AssertSecretFree(harness, exception.ToString());
        Assert.DoesNotContain("website/password", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertSecretFree(BridgeTestHarness harness, params string[] outputs)
    {
        Assert.All(outputs, output => Assert.DoesNotContain(
            BridgeTestHarness.SecretValue,
            output,
            StringComparison.Ordinal));
        Assert.All(harness.Logger.Entries, entry => Assert.DoesNotContain(
            BridgeTestHarness.SecretValue,
            entry.Message,
            StringComparison.Ordinal));
    }
}
