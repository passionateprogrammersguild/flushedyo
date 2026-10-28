namespace FlushedYo.IntegrationTests;

/// <summary>Exit codes when a secret cannot be resolved or the key argument is missing.</summary>
public sealed class CliMissingSecretTests
{
    [Theory]
    [InlineData("not-found")]
    [InlineData("empty")]
    public async Task GetSecret_MissingSecret_ReturnsExitCodeOne(string mode)
    {
        var (exitCode, _, stderr) = await CliRunner.RunAsync(["get-secret", "mykey"], CliRunner.FakeEnv(mode));

        Assert.Equal(1, exitCode);
        Assert.Contains("not found", stderr);
    }

    [Fact]
    public async Task GetSecret_MissingKey_ReturnsExitCodeOne()
    {
        var (exitCode, _, _) = await CliRunner.RunAsync(["get-secret"]);
        Assert.Equal(1, exitCode);
    }
}
