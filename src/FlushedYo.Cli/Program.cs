using FlushedYo.Core.Commands;
using FlushedYo.Core.Configuration;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,

    // Root the host at the executable directory (not the current working directory)
    // so the appsettings.json copied to the output folder loads no matter where
    // the CLI is invoked from.
    ContentRootPath = AppContext.BaseDirectory,
});

// Route console logs to stderr so stdout is reserved for command output.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// The host chains appsettings.json, environment variables (FLUSHEDYO__SECTION__KEY,
// standard __ delimiter, no prefix), and command-line arguments into one configuration.
builder.Services.Configure<FlushedYoOptions>(builder.Configuration.GetSection(FlushedYoOptions.SectionName));
builder.Services.AddSingleton<PluginLoader>();
builder.Services.AddSingleton<IPluginCatalog, PluginCatalog>();

// The ledger is opened lazily: nothing touches the database until a command uses the store.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LedgerDatabase>();
builder.Services.AddSingleton<ITransactionStore, SqliteTransactionStore>();
builder.Services.AddSingleton<TransactionTracker>();
builder.Services.AddSingleton<IRunHistoryStore, SqliteRunHistoryStore>();

// Plugins are loaded from their folders at run time; nothing here names a specific plugin.
builder.Services.AddSingleton<IPluginAssemblyLoader, PluginAssemblyLoader>();
builder.Services.AddSingleton<LoginService>();
builder.Services.AddSingleton<DownloadService>();
builder.Services.AddSingleton<StatusService>();
builder.Services.AddSingleton<LoginCommand>();
builder.Services.AddSingleton<DownloadCommand>();
builder.Services.AddSingleton<StatusCommand>();

builder.Services.AddSingleton<EnvironmentVariableSecretStore>();
builder.Services.AddSingleton<BitwardenMcpSecretStore>();
builder.Services.AddSingleton<ISecretStore>(static serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FlushedYoOptions>>().Value;
    return options.SecretStore.Provider.Equals(SecretStoreProviders.EnvironmentVariables, StringComparison.OrdinalIgnoreCase)
        ? (ISecretStore)serviceProvider.GetRequiredService<EnvironmentVariableSecretStore>()
        : serviceProvider.GetRequiredService<BitwardenMcpSecretStore>();
});

using var host = builder.Build();

if (args.Length == 0)
{
    return PrintUsage();
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    if (args[0].Equals("get-secret", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: flushedyo get-secret <key> [--raw]");
            return 1;
        }

        var secret = await host.Services.GetRequiredService<ISecretStore>().GetSecretAsync(args[1], cts.Token);
        if (secret is null)
        {
            Console.Error.WriteLine($"Secret '{args[1]}' was not found in the configured secret store.");
            return 1;
        }

        // Secrets print masked unless --raw is passed explicitly.
        Console.WriteLine(args.Contains("--raw") ? secret : new string('*', Math.Max(secret.Length, 4)));
        return 0;
    }

    if (args[0].Equals("plugins", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length == 1)
        {
            return PrintUsage(isError: true);
        }

        if (!args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown plugins subcommand '{args[1]}'.");
            return PrintUsage(isError: true);
        }

        if (args.Length > 2)
        {
            Console.Error.WriteLine("Unexpected arguments.");
            return PrintUsage(isError: true);
        }

        var plugins = await host.Services.GetRequiredService<IPluginCatalog>().GetPluginsAsync(cts.Token);
        Console.WriteLine(PluginTableFormatter.Format(plugins));
        return 0;
    }

    if (args[0].Equals("login", StringComparison.OrdinalIgnoreCase))
    {
        return await RunCommandAsync<LoginCommand>((command, options) => command.ExecuteAsync(options, Console.Out, Console.Error, cts.Token));
    }

    if (args[0].Equals("download", StringComparison.OrdinalIgnoreCase))
    {
        return await RunCommandAsync<DownloadCommand>((command, options) => command.ExecuteAsync(options, Console.Out, Console.Error, cts.Token));
    }

    if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
    {
        return await RunCommandAsync<StatusCommand>((command, options) => command.ExecuteAsync(options, Console.Out, Console.Error, cts.Token));
    }

    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    return PrintUsage(isError: true);
}
catch (OperationCanceledException)
{
    return 130;
}
catch (SecretStoreException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"unexpected error: {ex.Message}");
    return 2;
}

// login, download, and status share one shape: parse the flags after the command name, then run the command.
async Task<int> RunCommandAsync<TCommand>(Func<TCommand, CommandLineOptions, Task<int>> run)
    where TCommand : notnull
{
    if (!CommandLineParser.TryParse(args[1..], out var options, out var error))
    {
        Console.Error.WriteLine(error);
        return PrintUsage(isError: true);
    }

    return await run(host.Services.GetRequiredService<TCommand>(), options);
}

static int PrintUsage(bool isError = false)
{
    var writer = isError ? Console.Error : Console.Out;
    writer.WriteLine("flushedyo - personal finance CLI (Phase 4)");
    writer.WriteLine();
    writer.WriteLine("Usage:");
    writer.WriteLine("  flushedyo get-secret <key> [--raw]              Resolve a secret from the configured secret store.");
    writer.WriteLine("  flushedyo plugins list                          List discovered plugins.");
    writer.WriteLine("  flushedyo login [--plugin <name>] [--json]      Check that each plugin's credentials exist in the secret store.");
    writer.WriteLine("  flushedyo download [--plugin <name>] [--json]   Download transactions and store the new ones (deduplicated).");
    writer.WriteLine("  flushedyo status [--plugin <name>] [--json]     Show each plugin's last download and stored totals.");
    return isError ? 1 : 0;
}
