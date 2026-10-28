using FlushedYo.Core.SecretStore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Secret resolution and error mapping over the in-memory MCP transport. Preflight, timeout and
/// option-validation behavior live in their own classes so xUnit can run them concurrently.
/// </summary>
public sealed class BitwardenMcpSecretStoreTests
{
    [Fact]
    public async Task GetSecretAsync_ResolvesViaMcpTool()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            McpServerTool.Create(
                (string @object, string id) => @object == "password" && id == "Fidelity" ? "hunter2" : "",
                new() { Name = "get" }));

        await using (server)
        {
            var secret = await store.GetSecretAsync("Fidelity", TestContext.Current.CancellationToken);

            Assert.Equal("hunter2", secret);
            Assert.True(await store.SecretExistsAsync("Fidelity", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull_WhenToolReturnsNoText()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            McpServerTool.Create((string @object, string id) => "", new() { Name = "get" }));

        await using (server)
        {
            Assert.Null(await store.GetSecretAsync("unknown-item", TestContext.Current.CancellationToken));
            Assert.False(await store.SecretExistsAsync("unknown-item", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task GetSecretAsync_ThrowsSecretStoreException_OnToolError()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            McpServerTool.Create(
                new Func<string, string, string>((@object, id) => throw new InvalidOperationException("vault is locked")),
                new() { Name = "get" }));

        await using (server)
        {
            var exception = await Assert.ThrowsAsync<SecretStoreException>(
                () => store.GetSecretAsync("Fidelity", TestContext.Current.CancellationToken));

            Assert.Contains("get", exception.Message);
        }
    }

    [Fact]
    public async Task GetSecretAsync_NotFoundText_ReturnsNull()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            McpServerTool.Create(
                new Func<string, string, Task<string>>(async (@object, id) => { await Task.Yield(); throw new McpException("Not found."); }),
                new() { Name = "get" }));

        await using (server)
        {
            Assert.Null(await store.GetSecretAsync("k", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task GetSecretAsync_VaultLockedErrorText_Throws()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            McpServerTool.Create(
                new Func<string, string, Task<string>>(async (@object, id) => { await Task.Yield(); throw new McpException("Vault is locked"); }),
                new() { Name = "get" }));

        await using (server)
        {
            var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("k", TestContext.Current.CancellationToken));
            Assert.Contains("Vault is locked", ex.Message);
        }
    }

    [Fact]
    public async Task GetSecretAsync_ToolError_IsNotDoubleWrapped()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            McpServerTool.Create(
                new Func<string, string, Task<string>>(async (@object, id) => { await Task.Yield(); throw new McpException("Vault error"); }),
                new() { Name = "get" }));

        await using (server)
        {
            var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("k", TestContext.Current.CancellationToken));
            Assert.Contains("Vault error", ex.Message);
            Assert.DoesNotContain("MCP call failed", ex.Message);
            Assert.Equal(1, ex.Message.Split("failed for").Length - 1);
        }
    }
}
