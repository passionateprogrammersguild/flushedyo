namespace FlushedYo.IntegrationTests;

/// <summary>Output formatting and startup-failure exit codes of the built CLI.</summary>
public sealed class CliEndToEndTests
{
    [Fact]
    public async Task GetSecret_Raw_PrintsExactValueOnly()
    {
        var (exitCode, stdout, stderr) = await CliRunner.RunAsync(["get-secret", "mykey", "--raw"], CliRunner.FakeEnv("ok"));

        Assert.Equal(0, exitCode);
        Assert.Equal(CliRunner.Secret + Environment.NewLine, stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task GetSecret_MaskedByDefault()
    {
        var (exitCode, stdout, stderr) = await CliRunner.RunAsync(["get-secret", "mykey"], CliRunner.FakeEnv("ok"));

        Assert.Equal(0, exitCode);
        Assert.Contains("****", stdout);
        Assert.DoesNotContain(CliRunner.Secret, stdout);
        Assert.DoesNotContain(CliRunner.Secret, stderr);
    }

    [Fact]
    public async Task GetSecret_MissingCommand_ReturnsExitCodeTwo()
    {
        var env = new Dictionary<string, string>
        {
            ["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__COMMAND"] = "nonexistent-binary-123",
            ["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__ENVIRONMENTVARIABLES__BW_SESSION"] = "dummy",
        };

        var (exitCode, _, stderr) = await CliRunner.RunAsync(["get-secret", "mykey"], env);

        Assert.Equal(2, exitCode);
        var lastLine = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.StartsWith("error:", lastLine);
    }

    [Fact]
    public async Task GetSecret_BwSessionUnset_ReturnsExitCodeTwo()
    {
        var env = CliRunner.FakeEnv("ok", withSession: false);
        env["FLUSHEDYO__SECRETSTORE__BITWARDENMCP__ENVIRONMENTVARIABLES__BW_SESSION"] = "${BW_SESSION}";

        var (exitCode, _, stderr) = await CliRunner.RunAsync(["get-secret", "mykey"], env);

        Assert.Equal(2, exitCode);
        Assert.Contains("BW_SESSION", stderr);
    }
}
