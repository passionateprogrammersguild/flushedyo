namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Caller-initiated cancellation. Tool calls signal a <see cref="TaskCompletionSource"/> once they
/// are in flight, so cancellation is deterministic instead of racing a fixed sleep.
/// </summary>
public sealed class BitwardenMcpSecretStoreCancellationTests
{
    [Fact]
    public async Task GetSecretAsync_CallerCancellationMidCall_Propagates()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.SignallingHangingGetTool(started));

        await using (server)
        {
            using var cts = new CancellationTokenSource();
            var task = store.GetSecretAsync("k", cts.Token);
            await InMemoryMcpServer.WaitForCallAsync(started);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
    }

    [Fact]
    public async Task GetSecretAsync_PreCancelledToken_Throws()
    {
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.OkGetTool());

        await using (server)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetSecretAsync("k", cts.Token));
        }
    }

    [Fact]
    public async Task Preflight_CallerCancellation_Propagates()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (store, server) = InMemoryMcpServer.Create(
            InMemoryMcpServer.TestOptions(),
            InMemoryMcpServer.SignallingHangingStatusTool(started),
            InMemoryMcpServer.OkGetTool());

        await using (server)
        {
            using var cts = new CancellationTokenSource();
            var task = store.GetSecretAsync("k", cts.Token);
            await InMemoryMcpServer.WaitForCallAsync(started);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
    }
}
