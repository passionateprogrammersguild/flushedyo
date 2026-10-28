namespace FlushedYo.IntegrationTests;

/// <summary>Argument-error and usage-text tests for the built CLI.</summary>
public sealed class CliUsageTests
{
    [Theory]
    [InlineData("download", "--bogus")]
    [InlineData("login", "--json", "--json")]
    [InlineData("status", "--plugin")]
    [InlineData("status", "extra")]
    public async Task InvalidArguments_ReturnExitOne_NoStdout_NoLedger(params string[] args)
    {
        using var workspace = new CliWorkspace();

        var result = await workspace.RunAsync(args);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage", result.StdErr);
        Assert.Empty(result.StdOut);
        Assert.False(workspace.LedgerExists);
    }

    [Fact]
    public async Task NoArguments_PrintsUsage_ExitZero()
    {
        using var workspace = new CliWorkspace();

        var result = await workspace.RunAsync([]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage", result.StdOut);
        Assert.Contains("login", result.StdOut);
        Assert.Contains("download", result.StdOut);
        Assert.Contains("status", result.StdOut);
        Assert.False(workspace.LedgerExists);
    }

    [Fact]
    public async Task GetSecretBehavior_Unchanged()
    {
        using var workspace = new CliWorkspace();

        var result = await workspace.RunAsync(["get-secret", "mykey", "--raw"], CliRunner.FakeEnv("ok"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(CliRunner.Secret + Environment.NewLine, result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Fact]
    public async Task PluginsListBehavior_Unchanged()
    {
        using var workspace = new CliWorkspace();
        workspace.AddDescriptorOnlyPlugin("stub");

        // No capabilities that need download, just tests that plugins list still works.
        var result = await workspace.RunAsync(["plugins", "list"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("stub", result.StdOut);
        Assert.Contains("NAME", result.StdOut);
    }
}
