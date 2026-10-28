namespace FlushedYo.Core.Configuration;

/// <summary>
/// Root options for flushedyo, bound from the "FlushedYo" configuration section
/// (appsettings.json) with environment-variable overrides using the standard
/// __ delimiter (e.g. FLUSHEDYO__SECRETSTORE__PROVIDER).
/// </summary>
public sealed class FlushedYoOptions
{
    public const string SectionName = "FlushedYo";

    public SecretStoreOptions SecretStore { get; set; } = new();

    /// <summary>
    /// Directory containing plugin folders (plugin.json + config.json + skills/ + tools/).
    /// A relative path resolves against the repository root (the nearest ancestor of the current
    /// directory containing flushedyo.slnx); an absolute path is used as-is.
    /// </summary>
    public string PluginDirectory { get; set; } = "plugins";

    /// <summary>
    /// Path to the local SQLite ledger used for deduplicated transactions. A relative path resolves
    /// against the repository root (the nearest ancestor of the current directory containing
    /// flushedyo.slnx); an absolute path is used as-is. The parent directory is created on first use.
    /// </summary>
    public string LedgerPath { get; set; } = "data/flushedyo.db";
}

public sealed class SecretStoreOptions
{
    public string Provider { get; set; } = SecretStoreProviders.BitwardenMcp;

    public BitwardenMcpOptions BitwardenMcp { get; set; } = new();
}

public static class SecretStoreProviders
{
    public const string BitwardenMcp = "BitwardenMcp";
    public const string EnvironmentVariables = "EnvironmentVariables";
}

/// <summary>
/// Configuration for the Bitwarden MCP secret store. Supports two transports:
/// stdio (launch <see cref="Command"/>, e.g. npx -y @bitwarden/mcp-server) or
/// HTTP (connect to <see cref="Url"/>, which takes precedence).
/// </summary>
public sealed class BitwardenMcpOptions
{
    /// <summary>stdio: executable used to launch the Bitwarden MCP server, e.g. "npx".</summary>
    public string? Command { get; set; }

    /// <summary>
    /// stdio: arguments passed to <see cref="Command"/>. Empty by default (appsettings.json supplies
    /// <c>-y @bitwarden/mcp-server</c>) because the configuration binder appends to non-empty list defaults.
    /// </summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>http: endpoint of an already-running Bitwarden MCP server. Takes precedence over <see cref="Command"/>.</summary>
    public string? Url { get; set; }

    /// <summary>MCP tool invoked to resolve a secret. Default targets the official server's <c>get</c> tool.</summary>
    public string GetSecretTool { get; set; } = "get";

    /// <summary>Name of the tool argument that receives the secret key.</summary>
    public string SecretKeyArgument { get; set; } = "id";

    /// <summary>
    /// Static tool arguments merged into every call. Defaults are equivalent to
    /// <c>bw get password &lt;id&gt;</c> on the official Bitwarden MCP server.
    /// </summary>
    public Dictionary<string, string> ToolArguments { get; set; } = new() { ["object"] = "password" };

    /// <summary>
    /// Environment variables passed to the stdio server process.
    /// Values support ${VAR} expansion from the parent process environment
    /// (e.g. "BW_SESSION": "${BW_SESSION}").
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new() { ["BW_SESSION"] = "${BW_SESSION}" };

    /// <summary>Timeout in seconds for the initial connection and handshake. Default is 30.
    /// Note: The first run of 'npx -y' may take longer as it downloads the package.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>Timeout in seconds for individual tool calls. Default is 30.</summary>
    public int CallTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// MCP tool used for the preflight check to detect if the vault is locked.
    /// Default is "status". Set to empty to disable preflight.
    /// </summary>
    public string PreflightStatusTool { get; set; } = "status";
}
