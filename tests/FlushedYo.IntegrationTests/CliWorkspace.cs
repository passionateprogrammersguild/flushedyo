using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// A self-contained temp workspace for CLI end-to-end tests: a plugin directory and a ledger
/// path under a temp root (deleted on dispose). All variables reach the child process through
/// the <see cref="CliRunner"/> env dictionary; the test process environment is never mutated.
/// </summary>
internal sealed class CliWorkspace : IDisposable
{
    /// <summary>Temp root directory, deleted on dispose.</summary>
    public string Root { get; }

    /// <summary><c>&lt;Root&gt;/plugins</c>, created on construction.</summary>
    public string PluginsRoot { get; }

    /// <summary><c>&lt;Root&gt;/data/ledger.db</c>. Parent directory is not created until a command writes the ledger.</summary>
    public string LedgerPath { get; }

    /// <summary>True when the ledger file exists.</summary>
    public bool LedgerExists => File.Exists(LedgerPath);

    /// <summary>True when the ledger's parent directory exists.</summary>
    public bool LedgerDirectoryExists => Directory.Exists(Path.GetDirectoryName(LedgerPath)!);

    /// <summary>Creates a fresh temp workspace.</summary>
    public CliWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "flushedyo-cli-" + Guid.NewGuid().ToString("N"));
        PluginsRoot = Path.Combine(Root, "plugins");
        LedgerPath = Path.Combine(Root, "data", "ledger.db");
        Directory.CreateDirectory(PluginsRoot);
    }

    /// <summary>
    /// Builds the environment dictionary passed to the child process: the plugin directory and
    /// ledger path are always set; <paramref name="extra"/> is merged on top. The secret-store
    /// provider is left as the production default (BitwardenMcp) unless <paramref name="extra"/>
    /// overrides it.
    /// </summary>
    public Dictionary<string, string> Env(Dictionary<string, string>? extra = null)
    {
        var env = new Dictionary<string, string>
        {
            ["FLUSHEDYO__PLUGINDIRECTORY"] = PluginsRoot,
            ["FLUSHEDYO__LEDGERPATH"] = LedgerPath,
            // The default provider is BitwardenMcp; its constructor validates BW_SESSION is set.
            // These tests never contact the store (the stub plugins don't request secrets), so a
            // dummy value lets the container build without starting npx or the fake MCP server.
            ["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__ENVIRONMENTVARIABLES__BW_SESSION"] = "dummy",
        };
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                env[key] = value;
            }
        }

        return env;
    }

    /// <summary>Runs the built CLI with the workspace's plugin directory and ledger path.</summary>
    public Task<CliResult> RunAsync(string[] args, Dictionary<string, string>? extra = null) =>
        CliRunner.RunAsync(args, Env(extra));

    /// <summary>Installs a runnable stub plugin (manifest + assembly + data file).</summary>
    public string AddStubPlugin(
        string name,
        string? stubJson = null,
        string type = StubPluginFixture.DefaultType,
        string? credentialKeysJson = null) =>
        StubPluginFixture.CreatePlugin(PluginsRoot, name, stubJson, type, credentialKeysJson: credentialKeysJson);

    /// <summary>Installs a descriptor-only plugin (no executable assembly).</summary>
    public string AddDescriptorOnlyPlugin(string name) =>
        StubPluginFixture.CreateDescriptorOnlyPlugin(PluginsRoot, name);

    /// <summary>Removes a plugin folder. After this, <c>download</c> will not list the plugin.</summary>
    public void RemovePlugin(string name)
    {
        var path = Path.Combine(PluginsRoot, name);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Environment variables that switch the secret store to <see cref="EnvironmentVariableSecretStore"/>:
    /// <c>FLUSHEDYO__SECRETSTORE__PROVIDER=EnvironmentVariables</c> plus one
    /// <c>FLUSHEDYO_SECRET_&lt;variable&gt;</c> entry per tuple. Use the variable name with slashes replaced
    /// by underscores and uppercased (e.g. <c>STUB_USERNAME</c> for key <c>stub/username</c>).
    /// </summary>
    public static Dictionary<string, string> EnvironmentSecretStore(params (string Variable, string Value)[] secrets)
    {
        var env = new Dictionary<string, string>
        {
            ["FLUSHEDYO__SECRETSTORE__PROVIDER"] = SecretStoreProviders.EnvironmentVariables,
        };
        foreach (var (variable, value) in secrets)
        {
            env[EnvironmentVariableSecretStore.Prefix + variable.ToUpperInvariant()] = value;
        }

        return env;
    }

    /// <summary>Deletes the temp root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
