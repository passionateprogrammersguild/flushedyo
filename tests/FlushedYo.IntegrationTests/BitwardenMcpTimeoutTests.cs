namespace FlushedYo.IntegrationTests;

using FlushedYo.Core.SecretStore;

/// <summary>
/// Timeout behavior that only a real child process can exercise: a server that never completes the
/// MCP handshake.
/// </summary>
public sealed class BitwardenMcpTimeoutTests
{
    [Fact]
    public async Task GetSecret_ConnectTimeout_ThrowsTimeout()
    {
        var store = StoreFactory.CreateFake("hang-on-init", connectTimeout: 1);
        var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("any", TestContext.Current.CancellationToken));
        Assert.Contains("Timed out after 1s connecting", ex.Message);
    }
}
