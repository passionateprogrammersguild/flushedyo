using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// The connect timeout. Each timeout scenario is its own class so xUnit runs them concurrently
/// instead of serializing the whole second of waiting behind one another.
/// </summary>
public sealed class BitwardenMcpConnectTimeoutTests
{
    [Fact]
    public async Task GetSecretAsync_TimesOut_WhenServerHangs()
    {
        var options = InMemoryMcpServer.TestOptions();
        options.ConnectTimeoutSeconds = 1;

        // Client factory that never completes
        var store = new BitwardenMcpSecretStore(
            options,
            async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null!;
            },
            NullLogger<BitwardenMcpSecretStore>.Instance);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("Fidelity", TestContext.Current.CancellationToken));
        Assert.Contains("Timed out after 1s connecting", exception.Message);
    }
}

/// <summary>The per-call timeout for the secret lookup.</summary>
public sealed class BitwardenMcpCallTimeoutTests
{
    [Fact]
    public async Task GetSecretAsync_CallTimeout_ThrowsSecretStoreException()
    {
        var options = InMemoryMcpServer.TestOptions();
        options.CallTimeoutSeconds = 1;

        var (store, server) = InMemoryMcpServer.Create(options, InMemoryMcpServer.HangingGetTool());
        await using (server)
        {
            var exception = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("k", TestContext.Current.CancellationToken));
            Assert.Contains("Timed out after 1s waiting", exception.Message);
        }
    }
}

/// <summary>The preflight call's timeout, which must not be reported as a generic tool failure.</summary>
public sealed class BitwardenMcpPreflightTimeoutTests
{
    [Fact]
    public async Task Preflight_Timeout_ThrowsSecretStoreException()
    {
        var options = InMemoryMcpServer.TestOptions();
        options.CallTimeoutSeconds = 1;

        var (store, server) = InMemoryMcpServer.Create(
            options,
            InMemoryMcpServer.HangingStatusTool(),
            InMemoryMcpServer.OkGetTool());

        await using (server)
        {
            var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("k", TestContext.Current.CancellationToken));
            Assert.Contains("Preflight check timed out", ex.Message);
        }
    }
}
