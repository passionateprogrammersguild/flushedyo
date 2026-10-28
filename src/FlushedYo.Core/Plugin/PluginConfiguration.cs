namespace FlushedYo.Core.Plugin;

/// <summary>
/// Typed contents of a plugin's optional <c>config.json</c> file (plan §7).
/// Unknown keys are ignored.
/// </summary>
public sealed record PluginConfiguration
{
    /// <summary>The configuration used when a plugin has no <c>config.json</c>.</summary>
    public static PluginConfiguration Default { get; } = new();

    /// <summary>Endpoint of the Bitwarden MCP server, when the plugin configures one.</summary>
    public string? BitwardenMcpUrl { get; init; }

    /// <summary>Endpoint of the Playwright MCP server, when the plugin configures one.</summary>
    public string? PlaywrightMcpUrl { get; init; }

    /// <summary>Whether browser automation runs headless. Defaults to <see langword="true"/>.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>Secret-store key names the plugin uses to resolve its credentials.</summary>
    public PluginCredentialKeys CredentialKeys { get; init; } = new();
}

/// <summary>
/// Secret-store key names (not values) a plugin uses to look up its credentials.
/// </summary>
public sealed record PluginCredentialKeys
{
    /// <summary>Secret-store key for the account username, for example <c>fidelity/username</c>.</summary>
    public string? Username { get; init; }

    /// <summary>Secret-store key for the account password, for example <c>fidelity/password</c>.</summary>
    public string? Password { get; init; }
}
