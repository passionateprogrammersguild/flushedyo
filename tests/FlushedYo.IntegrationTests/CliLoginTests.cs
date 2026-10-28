using System.Text.Json;

namespace FlushedYo.IntegrationTests;

/// <summary>End-to-end <c>login</c> tests against the built CLI with stub plugins.</summary>
public sealed class CliLoginTests
{
    private const string CredentialKeysJson = """{"username":"stub/username","password":"stub/password"}""";

    private static async Task<PluginStatusRow[]> ParseJsonAsync(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var ok = root.GetProperty("ok").GetBoolean();
        var plugins = root.GetProperty("plugins").EnumerateArray().Select(plugin => new PluginStatusRow
        {
            Ok = ok,
            Plugin = plugin.GetProperty("plugin").GetString()!,
            Credentials = plugin.GetProperty("credentials").EnumerateArray().Select(cred => cred.GetProperty("status").GetString()!).ToArray(),
        }).ToArray();
        return plugins;
    }

    [Fact]
    public async Task AllCredentialsPresent_ExitsZero_NoSecretInOutput()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: CredentialKeysJson);

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(
            ("STUB_USERNAME", "S3CR3T-USER"),
            ("STUB_PASSWORD", "S3CR3T-PASS")));

        var result = await workspace.RunAsync(["login"], env);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ok", result.StdOut);
        Assert.Contains("stub/username", result.StdOut);
        Assert.Contains("stub/password", result.StdOut);
        Assert.DoesNotContain("S3CR3T-USER", result.StdOut + result.StdErr);
        Assert.Empty(result.StdErr);
    }

    [Fact]
    public async Task MissingCredential_ExitsOne_StderrHints()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: CredentialKeysJson);

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(("STUB_USERNAME", "S3CR3T-USER")));

        var result = await workspace.RunAsync(["login"], env);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("missing", result.StdOut);
        Assert.Contains("Some credentials were not found", result.StdErr);
        Assert.DoesNotContain("S3CR3T-USER", result.StdOut + result.StdErr);
    }

    [Fact]
    public async Task Json_AllPresent_OkTrue()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: CredentialKeysJson);

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(
            ("STUB_USERNAME", "S3CR3T-USER"),
            ("STUB_PASSWORD", "S3CR3T-PASS")));

        var result = await workspace.RunAsync(["login", "--json"], env);

        Assert.Equal(0, result.ExitCode);
        var rows = await ParseJsonAsync(result.StdOut);
        Assert.Single(rows);
        Assert.True(rows[0].Ok);
        Assert.All(rows[0].Credentials, status => Assert.Equal("ok", status));
        Assert.Empty(result.StdErr);
    }

    [Fact]
    public async Task Json_MissingCredential_OkFalse_ExitOne()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: CredentialKeysJson);

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(("STUB_USERNAME", "S3CR3T-USER")));

        var result = await workspace.RunAsync(["login", "--json"], env);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var statuses = doc.RootElement.GetProperty("plugins")[0].GetProperty("credentials")
            .EnumerateArray().Select(c => c.GetProperty("status").GetString()).ToArray();
        Assert.Contains("ok", statuses);
        Assert.Contains("missing", statuses);
    }

    [Fact]
    public async Task NotConfigured_ExitsZero_AllRowsNotConfigured()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub"); // no credential keys

        var env = workspace.Env();

        var result = await workspace.RunAsync(["login"], env);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("not-configured", result.StdOut);
        Assert.Contains("not-configured", result.StdOut); // twice
    }

    [Fact]
    public async Task DescriptorOnlyPlugin_CanLogin()
    {
        using var workspace = new CliWorkspace();
        var folder = workspace.AddDescriptorOnlyPlugin("stub");
        File.WriteAllText(Path.Combine(folder, "config.json"), $$"""{ "credentialKeys": {{CredentialKeysJson}} }""");

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(
            ("STUB_USERNAME", "S3CR3T-USER"),
            ("STUB_PASSWORD", "S3CR3T-PASS")));

        var result = await workspace.RunAsync(["login"], env);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ok", result.StdOut);
        Assert.DoesNotContain("S3CR3T", result.StdOut + result.StdErr);
    }

    [Fact]
    public async Task LockedVault_ExitsTwo()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: CredentialKeysJson);

        var result = await workspace.RunAsync(["login"], CliRunner.FakeEnv("locked"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("vault is locked", result.StdErr);
    }

    [Fact]
    public async Task UnknownPlugin_ExitsOne()
    {
        using var workspace = new CliWorkspace();

        var result = await workspace.RunAsync(["login", "--plugin", "nope"], workspace.Env());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("was not found", result.StdErr);
    }

    [Fact]
    public async Task Login_NeverCreatesTheLedger()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: CredentialKeysJson);

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(
            ("STUB_USERNAME", "S3CR3T-USER"),
            ("STUB_PASSWORD", "S3CR3T-PASS")));

        await workspace.RunAsync(["login", "--json"], env);

        Assert.False(workspace.LedgerExists);
        Assert.False(workspace.LedgerDirectoryExists);
        await workspace.RunAsync(["login"], env);
        Assert.False(workspace.LedgerExists);
        Assert.False(workspace.LedgerDirectoryExists);
    }

    private sealed class PluginStatusRow
    {
        public bool Ok { get; init; }
        public string Plugin { get; init; } = "";
        public string[] Credentials { get; init; } = [];
    }
}
