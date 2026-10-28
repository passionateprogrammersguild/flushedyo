using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Missing-variable and blank-key boundary specs (spec: secret_store).
/// Serialized with the other tests that mutate process-wide environment variables.
/// The key-to-variable mapping is covered as a property in
/// <c>FlushedYo.Core.PropertyTests.SecretStore.EnvironmentVariableSecretStoreProperties</c>.
/// </summary>
[Collection("EnvironmentMutation")]
public sealed class EnvironmentVariableSecretStoreTests
{
    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenVariableMissing()
    {
        var store = new EnvironmentVariableSecretStore();

        Assert.Null(await store.GetSecretAsync("definitely/not/set", TestContext.Current.CancellationToken));
        Assert.False(await store.SecretExistsAsync("definitely/not/set", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankKey_Throws(string key)
    {
        var store = new EnvironmentVariableSecretStore();
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetSecretAsync(key, token));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SecretExistsAsync(key, token));
    }
}
