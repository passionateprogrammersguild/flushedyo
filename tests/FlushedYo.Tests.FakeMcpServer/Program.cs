using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;


var mode = args.Length > 0 ? args[0] : "ok";

if (mode == "exit-on-start")
{
    Console.Error.WriteLine("Fake server crashing on start");
    Environment.Exit(3);
}

if (mode == "hang-on-init")
{
    // Never answer 'initialize'. Drain stdin so the client's graceful shutdown (which closes
    // stdin) ends this process immediately instead of waiting out the transport's 5s
    // ShutdownTimeout before killing it.
    await Console.OpenStandardInput().CopyToAsync(Stream.Null);
    return;
}

var transport = new StdioServerTransport(new McpServerOptions(), null);

var server = McpServer.Create(
    transport,
    new McpServerOptions
    {
        ToolCollection = [
            McpServerTool.Create(
                async (CancellationToken ct, string @object, string id) =>
                {
                    return mode switch
                    {
                        "ok" => "fake-secret-value",
                        "not-found" => throw new McpException("Not found."),
                        "tool-error" => throw new McpException("Vault error"),
                        "empty" => "",
                        "locked" or "unauthenticated" => throw new McpException("get must not be called while the vault is locked."),
                        "crash-on-call" => throw new InvalidOperationException("Simulated crash during call"),
                        "echo-session" => Environment.GetEnvironmentVariable("BW_SESSION") != null ? "SESSION_SET" : "SESSION_UNSET",
                        _ => ""
                    };
                },
                new() { Name = "get" }),
            McpServerTool.Create(
                () =>
                {
                    return mode switch
                    {
                        "ok" => "{\"status\":\"unlocked\"}",
                        "locked" => "{\"status\":\"locked\"}",
                        "unauthenticated" => "{\"status\":\"unauthenticated\"}",
                        _ => "{\"status\":\"unknown\"}"
                    };
                },
                new() { Name = "status" })
        ]
    },
    null,
    null);

await server.RunAsync();
