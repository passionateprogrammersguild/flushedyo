using FlushedYo.Core.SecretStore;
using ModelContextProtocol.Server;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// The preflight <c>status</c> check that detects a locked vault before the secret is requested.
/// </summary>
public sealed class BitwardenMcpSecretStorePreflightTests
{
    [Fact]
    public async Task Preflight_Unlocked_Passes()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.StatusTool(_ => Task.FromResult("{\"status\":\"unlocked\"}")),
            InMemoryMcpServer.OkGetTool());

        await using (server)
        {
            Assert.Equal("value", await store.GetSecretAsync("k", TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("unauthenticated")]
    public async Task Preflight_LockedOrUnauthenticated_Throws_AndNeverCallsGet(string status)
    {
        var getCalled = false;
        var get = McpServerTool.Create(
            (string @object, string id) =>
            {
                getCalled = true;
                return "value";
            },
            new() { Name = "get" });

        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.StatusTool(_ => Task.FromResult($"{{\"status\":\"{status}\"}}")),
            get);

        await using (server)
        {
            var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("k", TestContext.Current.CancellationToken));
            Assert.Contains("vault is locked", ex.Message);
            Assert.False(getCalled, "the 'get' tool must not run while the vault is locked");
        }
    }

    [Fact]
    public async Task Preflight_UnparseableOutput_Proceeds()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.StatusTool(_ => Task.FromResult("gibberish")),
            InMemoryMcpServer.OkGetTool());

        await using (server)
        {
            Assert.Equal("value", await store.GetSecretAsync("k", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Preflight_MissingStatusTool_Proceeds()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.OkGetTool());

        await using (server)
        {
            Assert.Equal("value", await store.GetSecretAsync("k", TestContext.Current.CancellationToken));
        }
    }
}
