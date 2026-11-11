using FlushedYo.Core.Commands;
using FlushedYo.Core.SecretStore;
using static FlushedYo.Core.Tests.Commands.CommandTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Credential existence checks (spec: login_credential_preflight).</summary>
public sealed class LoginServiceTests
{
    private const string SecretValue = "S3CR3T-VALUE";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeSecretStore StoreWith(params string[] keys) =>
        new(keys.ToDictionary(static key => key, static _ => SecretValue));

    private static async Task<LoginReport> CheckUnconfiguredAsync(string? key)
    {
        var store = StoreWith();
        var report = await new LoginService(store).CheckAsync([Descriptor("a", key)], Token);

        Assert.Empty(store.CheckedKeys);
        var check = Assert.Single(Assert.Single(report.Plugins).Credentials);
        Assert.Equal(new CredentialCheck("credential", null, CredentialStatus.NotConfigured), check);
        return report;
    }

    [Fact]
    public async Task CredentialPresent_IsOk()
    {
        var store = StoreWith("a/user");

        var report = await new LoginService(store).CheckAsync([Descriptor("a", "a/user")], Token);

        var result = Assert.Single(report.Plugins);
        Assert.Equal([new CredentialCheck("credential", "a/user", CredentialStatus.Ok)], result.Credentials);
        Assert.Equal(["a/user"], store.CheckedKeys);
        Assert.False(report.HasMissing);
    }

    [Fact]
    public async Task CredentialMissing_IsReported()
    {
        var report = await new LoginService(StoreWith()).CheckAsync([Descriptor("a", "a/user")], Token);

        var check = Assert.Single(Assert.Single(report.Plugins).Credentials);
        Assert.Equal(CredentialStatus.Missing, check.Status);
        Assert.True(report.HasMissing);
    }

    [Fact]
    public async Task NoKeyConfigured_IsNotConfigured_AndTheStoreIsNeverAsked()
    {
        var report = await CheckUnconfiguredAsync(null);

        Assert.False(report.HasMissing);
    }

    [Fact]
    public async Task BlankKey_IsNotConfigured_AndTheStoreIsNeverAsked()
    {
        var report = await CheckUnconfiguredAsync(" \t");

        Assert.Equal("a", Assert.Single(report.Plugins).Plugin);
    }

    [Fact]
    public async Task Plugins_KeepInputOrder_WithOneCredentialEach()
    {
        var report = await new LoginService(StoreWith("z/u", "a/u"))
            .CheckAsync([Descriptor("zeta", "z/u"), Descriptor("alpha", "a/u")], Token);

        Assert.Equal(["zeta", "alpha"], report.Plugins.Select(static plugin => plugin.Plugin).ToArray());
        Assert.All(report.Plugins, plugin => Assert.Equal("credential", Assert.Single(plugin.Credentials).Credential));
    }

    [Fact]
    public async Task GetSecretAsync_IsNeverCalled()
    {
        // FakeSecretStore.GetSecretAsync throws, so completing without an exception proves it was not called.
        var report = await new LoginService(StoreWith("a/user"))
            .CheckAsync([Descriptor("a", "a/user")], Token);

        Assert.False(report.HasMissing);
    }

    [Fact]
    public async Task DuplicateSecretKeyException_Propagates()
    {
        var store = new FakeSecretStore(existsFailure: new DuplicateSecretKeyException("a/user"));

        var exception = await Assert.ThrowsAsync<DuplicateSecretKeyException>(() =>
            new LoginService(store).CheckAsync([Descriptor("a", "a/user")], Token));

        Assert.Contains("a/user", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LoginService(StoreWith()).CheckAsync([Descriptor("a", "a/user")], cts.Token));
    }

    [Fact]
    public async Task NullPlugins_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new LoginService(StoreWith()).CheckAsync(null!, Token));
    }

    [Fact]
    public async Task Report_NeverContainsTheSecretValues()
    {
        var report = await new LoginService(StoreWith("a/user"))
            .CheckAsync([Descriptor("a", "a/user")], Token);

        Assert.DoesNotContain(SecretValue, CliJson.Serialize(report), StringComparison.Ordinal);
    }
}
