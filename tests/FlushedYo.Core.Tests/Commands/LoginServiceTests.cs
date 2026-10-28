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

    [Fact]
    public async Task BothKeysPresent_AreOk()
    {
        var report = await new LoginService(StoreWith("a/user", "a/pass"))
            .CheckAsync([Descriptor("a", "a/user", "a/pass")], Token);

        var result = Assert.Single(report.Plugins);
        Assert.Equal(
            [new CredentialCheck("username", "a/user", CredentialStatus.Ok), new CredentialCheck("password", "a/pass", CredentialStatus.Ok)],
            result.Credentials);
        Assert.False(report.HasMissing);
    }

    [Fact]
    public async Task OneKeyMissing_IsReported()
    {
        var report = await new LoginService(StoreWith("a/user"))
            .CheckAsync([Descriptor("a", "a/user", "a/pass")], Token);

        var checks = Assert.Single(report.Plugins).Credentials;
        Assert.Equal(CredentialStatus.Ok, checks[0].Status);
        Assert.Equal(CredentialStatus.Missing, checks[1].Status);
        Assert.True(report.HasMissing);
    }

    [Fact]
    public async Task NoKeysConfigured_AreNotConfigured_AndTheStoreIsNeverAsked()
    {
        var store = StoreWith();

        var report = await new LoginService(store).CheckAsync([Descriptor("a")], Token);

        var checks = Assert.Single(report.Plugins).Credentials;
        Assert.Equal(
            [new CredentialCheck("username", null, CredentialStatus.NotConfigured), new CredentialCheck("password", null, CredentialStatus.NotConfigured)],
            checks);
        Assert.Empty(store.CheckedKeys);
        Assert.False(report.HasMissing);
    }

    [Fact]
    public async Task OnlyAUsernameKey_ChecksTheUsernameAndLeavesThePasswordNotConfigured()
    {
        var store = StoreWith("a/user");

        var report = await new LoginService(store).CheckAsync([Descriptor("a", usernameKey: "a/user")], Token);

        var checks = Assert.Single(report.Plugins).Credentials;
        Assert.Equal(CredentialStatus.Ok, checks[0].Status);
        Assert.Equal(CredentialStatus.NotConfigured, checks[1].Status);
        Assert.Equal(["a/user"], store.CheckedKeys);
    }

    [Fact]
    public async Task Plugins_KeepInputOrder_WithUsernameBeforePassword()
    {
        var report = await new LoginService(StoreWith())
            .CheckAsync([Descriptor("zeta", "z/u", "z/p"), Descriptor("alpha", "a/u", "a/p")], Token);

        Assert.Equal(["zeta", "alpha"], report.Plugins.Select(static plugin => plugin.Plugin).ToArray());
        Assert.All(report.Plugins, plugin => Assert.Equal(["username", "password"], plugin.Credentials.Select(static check => check.Credential).ToArray()));
    }

    [Fact]
    public async Task GetSecretAsync_IsNeverCalled()
    {
        // FakeSecretStore.GetSecretAsync throws, so completing without an exception proves it was not called.
        var report = await new LoginService(StoreWith("a/user", "a/pass"))
            .CheckAsync([Descriptor("a", "a/user", "a/pass")], Token);

        Assert.False(report.HasMissing);
    }

    [Fact]
    public async Task SecretStoreException_Propagates()
    {
        var store = new FakeSecretStore(existsFailure: new SecretStoreException("vault is locked"));

        var exception = await Assert.ThrowsAsync<SecretStoreException>(() =>
            new LoginService(store).CheckAsync([Descriptor("a", "a/user", "a/pass")], Token));

        Assert.Equal("vault is locked", exception.Message);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LoginService(StoreWith()).CheckAsync([Descriptor("a", "a/user", "a/pass")], cts.Token));
    }

    [Fact]
    public async Task NullPlugins_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new LoginService(StoreWith()).CheckAsync(null!, Token));
    }

    [Fact]
    public async Task Report_NeverContainsTheSecretValues()
    {
        var report = await new LoginService(StoreWith("a/user", "a/pass"))
            .CheckAsync([Descriptor("a", "a/user", "a/pass")], Token);

        Assert.DoesNotContain(SecretValue, CliJson.Serialize(report), StringComparison.Ordinal);
    }
}
