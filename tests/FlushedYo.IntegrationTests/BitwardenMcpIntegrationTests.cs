using System.Diagnostics;
using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Process-boundary coverage for <see cref="BitwardenMcpSecretStore"/>: real child-process launch,
/// stdio wiring, exit codes and transport failures. Behavior that does not depend on the process
/// boundary lives in <c>FlushedYo.Core.Tests</c> over in-memory pipes.
/// </summary>
public sealed class BitwardenMcpIntegrationTests
{
    [Fact]
    public async Task GetSecret_HappyPath_ReturnsValue()
    {
        var store = StoreFactory.CreateFake("ok");
        Assert.Equal("fake-secret-value", await store.GetSecretAsync("any-key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSecret_MissingCommand_ThrowsCorrectError()
    {
        var store = StoreFactory.Create(new BitwardenMcpOptions
        {
            Command = "nonexistent-binary-12345",
            EnvironmentVariables = new Dictionary<string, string>(),
        });

        var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("any", TestContext.Current.CancellationToken));
        Assert.Contains("Could not start Bitwarden MCP server", ex.Message);
        Assert.Contains("command 'nonexistent-binary-12345' was not found", ex.Message);
    }

    [Fact]
    public async Task GetSecret_ServerExitsOnStart_ReportsExitCodeAndStderr()
    {
        var store = StoreFactory.CreateFake("exit-on-start");
        var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("any", TestContext.Current.CancellationToken));
        Assert.Contains("exit code 3", ex.Message);
        Assert.Contains("Fake server crashing on start", ex.Message);
    }

    [Fact]
    public async Task GetSecret_HttpToClosedPort_ThrowsSecretStoreException()
    {
        var store = StoreFactory.Create(new BitwardenMcpOptions
        {
            Url = "http://127.0.0.1:1",
            ConnectTimeoutSeconds = 10,
        });

        var started = Stopwatch.GetTimestamp();
        var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("any", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("was not found", ex.Message);
        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task GetSecret_ServerCrashesMidCall_ThrowsSecretStoreException()
    {
        var store = StoreFactory.CreateFake("crash-on-call");
        var ex = await Assert.ThrowsAsync<SecretStoreException>(() => store.GetSecretAsync("any", TestContext.Current.CancellationToken));
        Assert.Contains("'get'", ex.Message);
    }
}
