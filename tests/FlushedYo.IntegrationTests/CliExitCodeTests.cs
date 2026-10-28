namespace FlushedYo.IntegrationTests;

/// <summary>Exit codes for vault failures and argument/command errors of the built CLI.</summary>
public sealed class CliExitCodeTests
{
    [Fact]
    public async Task GetSecret_VaultLocked_ReturnsExitCodeTwo()
    {
        var (exitCode, _, stderr) = await CliRunner.RunAsync(["get-secret", "mykey"], CliRunner.FakeEnv("locked"));

        Assert.Equal(2, exitCode);
        Assert.Contains("vault is locked", stderr);
    }

    [Fact]
    public async Task UnknownCommand_ReturnsExitCodeOne()
    {
        var (exitCode, _, _) = await CliRunner.RunAsync(["bogus"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task NoArguments_ReturnsZero()
    {
        var (exitCode, _, _) = await CliRunner.RunAsync([]);
        Assert.Equal(0, exitCode);
    }
}
