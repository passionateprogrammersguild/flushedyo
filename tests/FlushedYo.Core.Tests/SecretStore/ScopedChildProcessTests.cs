using System.Runtime.InteropServices;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.SecretStore;

public sealed class ScopedChildProcessTests
{
    [Theory]
    [InlineData("OSX")]
    [InlineData("LINUX")]
    public void CreateStartInfo_Unix_InheritsOnlyRequiredNonSecretVariables(string platformName)
    {
        var environment = SensitiveEnvironment();
        var startInfo = ScopedChildProcess.CreateStartInfo(
            "native-backend",
            ["fixed-argument"],
            OSPlatform.Create(platformName),
            name => environment.TryGetValue(name, out var value) ? value : null,
            redirectStandardInput: true,
            redirectStandardOutput: true);

        Assert.Equal("native-backend", startInfo.FileName);
        Assert.Equal(["fixed-argument"], startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal("test-path", startInfo.Environment["PATH"]);
        Assert.Equal("test-home", startInfo.Environment["HOME"]);
        Assert.Equal("test-dbus", startInfo.Environment["DBUS_SESSION_BUS_ADDRESS"]);
        AssertSecretsAbsent(startInfo.Environment);
        Assert.False(startInfo.Environment.ContainsKey("SystemRoot"));
    }

    [Fact]
    public void CreateStartInfo_Windows_UsesPlatformApiSelectionAndScrubsSecrets()
    {
        var environment = SensitiveEnvironment();
        environment["SystemRoot"] = @"C:\Windows";
        environment["USERPROFILE"] = @"C:\Users\Test";
        var startInfo = ScopedChildProcess.CreateStartInfo(
            "native-backend",
            [],
            OSPlatform.Windows,
            name => environment.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(@"C:\Windows", startInfo.Environment["SystemRoot"]);
        Assert.Equal(@"C:\Users\Test", startInfo.Environment["USERPROFILE"]);
        Assert.Equal("test-path", startInfo.Environment["PATH"]);
        AssertSecretsAbsent(startInfo.Environment);
        Assert.False(startInfo.Environment.ContainsKey("DBUS_SESSION_BUS_ADDRESS"));
    }

    [Fact]
    public void GetCurrentPlatform_MatchesRuntimePlatformApis()
    {
        var platform = ScopedChildProcess.GetCurrentPlatform();

        Assert.True(
            platform == OSPlatform.Windows && OperatingSystem.IsWindows() ||
            platform == OSPlatform.OSX && OperatingSystem.IsMacOS() ||
            platform == OSPlatform.Linux && OperatingSystem.IsLinux() ||
            !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux());
    }

    private static Dictionary<string, string> SensitiveEnvironment() => new(StringComparer.Ordinal)
    {
        ["PATH"] = "test-path",
        ["HOME"] = "test-home",
        ["DBUS_SESSION_BUS_ADDRESS"] = "test-dbus",
        ["BW_SESSION"] = "synthetic-vault-session",
        ["FLUSHEDYO_SECRET_FIDELITY_PASSWORD"] = "synthetic-website-password",
        ["AWS_SECRET_ACCESS_KEY"] = "synthetic-unrelated-secret",
    };

    private static void AssertSecretsAbsent(IDictionary<string, string?> environment)
    {
        Assert.False(environment.ContainsKey("BW_SESSION"));
        Assert.False(environment.ContainsKey("FLUSHEDYO_SECRET_FIDELITY_PASSWORD"));
        Assert.False(environment.ContainsKey("AWS_SECRET_ACCESS_KEY"));
        Assert.Equal("1", environment["NO_COLOR"]);
        Assert.DoesNotContain(environment.Values, value => value?.Contains("synthetic", StringComparison.Ordinal) == true);
    }
}
