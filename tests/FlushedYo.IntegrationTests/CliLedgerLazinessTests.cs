namespace FlushedYo.IntegrationTests;

/// <summary>
/// The ledger services are registered but opened lazily: existing commands must never create the
/// database, and the container must stay valid. Variables go to the child process only.
/// </summary>
public sealed class CliLedgerLazinessTests
{
    [Fact]
    public async Task PluginsList_DoesNotCreateTheLedger()
    {
        using var temp = new TempDirectory();
        var env = LedgerEnv(temp);
        env["FLUSHEDYO__PLUGINDIRECTORY"] = temp.Combine("plugins");

        var result = await CliRunner.RunAsync(["plugins", "list"], env);

        Assert.Equal(0, result.ExitCode);
        AssertLedgerNotCreated(temp);
    }

    [Fact]
    public async Task GetSecret_DoesNotCreateTheLedger()
    {
        using var temp = new TempDirectory();
        var env = new Dictionary<string, string>(CliRunner.FakeEnv("ok"));
        foreach (var (key, value) in LedgerEnv(temp))
        {
            env[key] = value;
        }

        var result = await CliRunner.RunAsync(["get-secret", "mykey"], env);

        Assert.Equal(0, result.ExitCode);
        AssertLedgerNotCreated(temp);
    }

    [Fact]
    public async Task DevelopmentEnvironment_ValidatesTheContainerOnBuild()
    {
        using var temp = new TempDirectory();
        var env = LedgerEnv(temp);
        env["FLUSHEDYO__PLUGINDIRECTORY"] = temp.Combine("plugins");

        // The generic host enables ValidateOnBuild in Development, so Build() throws at startup
        // if any registered service cannot be constructed.
        env["DOTNET_ENVIRONMENT"] = "Development";

        var result = await CliRunner.RunAsync(["plugins", "list"], env);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No plugins installed.", result.StdOut);
    }

    [Fact]
    public async Task DevelopmentEnvironment_ValidatesStatusCommandRegistration()
    {
        using var workspace = new CliWorkspace();

        var env = workspace.Env();
        env["DOTNET_ENVIRONMENT"] = "Development";

        var result = await CliRunner.RunAsync(["status"], env);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Login_DoesNotCreateTheLedger()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub", credentialKeysJson: """{"username":"stub/username","password":"stub/password"}""");

        await workspace.RunAsync(["login", "--json"], CliWorkspace.EnvironmentSecretStore(
            ("STUB_USERNAME", "S3CR3T-USER"),
            ("STUB_PASSWORD", "S3CR3T-PASS")));

        Assert.False(workspace.LedgerExists);
        Assert.False(workspace.LedgerDirectoryExists);
    }

    [Fact]
    public async Task Status_DoesNotCreateTheLedger()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("stub");

        await workspace.RunAsync(["status", "--json"]);

        Assert.False(workspace.LedgerExists);
        Assert.False(workspace.LedgerDirectoryExists);
    }

    [Fact]
    public async Task Download_WithRunnablePlugin_CreatesTheLedger()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(workspace.LedgerExists);
        Assert.True(workspace.LedgerDirectoryExists);
    }

    [Fact]
    public async Task Download_WithOnlyDescriptorOnlyPlugins_DoesNotCreateTheLedger()
    {
        using var workspace = new CliWorkspace();
        workspace.AddDescriptorOnlyPlugin("notes");

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(workspace.LedgerExists);
    }

    private static Dictionary<string, string> LedgerEnv(TempDirectory temp) =>
        new() { ["FLUSHEDYO__LEDGERPATH"] = temp.Combine("nested", "ledger.db") };

    private static void AssertLedgerNotCreated(TempDirectory temp)
    {
        Assert.False(Directory.Exists(temp.Combine("nested")), "The ledger directory should not exist.");
        Assert.False(File.Exists(temp.Combine("nested", "ledger.db")), "The ledger file should not exist.");
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "flushedyo-cli-ledger-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(_path);

        public string Combine(params string[] parts) => Path.Combine([_path, .. parts]);

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}
