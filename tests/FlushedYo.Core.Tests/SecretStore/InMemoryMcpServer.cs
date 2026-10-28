using System.IO.Pipelines;
using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Wires a <see cref="BitwardenMcpSecretStore"/> to an in-process MCP server over in-memory pipes,
/// so store behavior is exercised through the real MCP client path without a child process.
/// </summary>
internal static class InMemoryMcpServer
{
    /// <summary>Options that point at a stdio command which is never actually launched.</summary>
    internal static BitwardenMcpOptions TestOptions() => new()
    {
        Command = "npx",
        EnvironmentVariables = new Dictionary<string, string>(),
        GetSecretTool = "get",
        SecretKeyArgument = "id",
        ToolArguments = new Dictionary<string, string> { ["object"] = "password" },
    };

    /// <summary>Creates a store and the server it talks to. Dispose the server when done.</summary>
    internal static (BitwardenMcpSecretStore Store, McpServer Server) Create(
        BitwardenMcpOptions options, params McpServerTool[] tools)
    {
        Pipe clientToServer = new(), serverToClient = new();

        var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            new McpServerOptions { ToolCollection = [.. tools] });
        _ = server.RunAsync();

        var store = new BitwardenMcpSecretStore(
            options,
            async cancellationToken => await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                cancellationToken: cancellationToken),
            NullLogger<BitwardenMcpSecretStore>.Instance);

        return (store, server);
    }

    /// <summary>A <c>get</c> tool that always returns "value".</summary>
    internal static McpServerTool OkGetTool() =>
        McpServerTool.Create((string @object, string id) => "value", new() { Name = "get" });

    /// <summary>A <c>get</c> tool that never completes.</summary>
    internal static McpServerTool HangingGetTool() =>
        McpServerTool.Create(
            async (CancellationToken ct, string @object, string id) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return "";
            },
            new() { Name = "get" });

    /// <summary>A <c>status</c> tool backed by <paramref name="handler"/>.</summary>
    internal static McpServerTool StatusTool(Func<CancellationToken, Task<string>> handler) =>
        McpServerTool.Create(async (CancellationToken ct) => await handler(ct), new() { Name = "status" });

    /// <summary>A <c>status</c> tool that never completes.</summary>
    internal static McpServerTool HangingStatusTool() =>
        StatusTool(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "";
        });

    /// <summary>
    /// A <c>get</c> tool that signals <paramref name="started"/> once the call is in flight, then
    /// hangs. Lets a test cancel deterministically instead of sleeping for an arbitrary delay.
    /// </summary>
    internal static McpServerTool SignallingHangingGetTool(TaskCompletionSource started) =>
        McpServerTool.Create(
            async (CancellationToken ct, string @object, string id) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return "";
            },
            new() { Name = "get" });

    /// <summary>A <c>status</c> tool that signals <paramref name="started"/> once the call is in flight, then hangs.</summary>
    internal static McpServerTool SignallingHangingStatusTool(TaskCompletionSource started) =>
        StatusTool(async ct =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return "";
        });

    /// <summary>Waits for a tool call to be in flight, failing the test instead of hanging forever.</summary>
    internal static async Task WaitForCallAsync(TaskCompletionSource started)
    {
        var completed = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completed == started.Task, "the MCP tool call did not start within 10s.");
    }
}
