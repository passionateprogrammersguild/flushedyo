namespace FlushedYo.IntegrationTests;

/// <summary>
/// End-to-end <c>plugins list</c> behavior against the built CLI. Plugin discovery needs no secret
/// store, so no fake MCP server is involved; each run points the CLI at an absolute plugin directory
/// through the environment of the child process only.
/// </summary>
public sealed class CliPluginListTests
{
    private static Dictionary<string, string> PluginDirectoryEnv(string directory) =>
        new() { ["FLUSHEDYO__PLUGINDIRECTORY"] = directory };

    [Fact]
    public async Task PluginsList_ListsFixturePlugin()
    {
        var result = await CliRunner.RunAsync(
            ["plugins", "list"],
            PluginDirectoryEnv(HelperPaths.GetPluginFixturesDirectory()));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("NAME", result.StdOut);
        Assert.Contains("VERSION", result.StdOut);
        Assert.Contains("CAPABILITIES", result.StdOut);
        Assert.Contains("dummy", result.StdOut);
        Assert.Contains("1.0.0", result.StdOut);
        Assert.Contains("download-transactions", result.StdOut);
        Assert.Contains("download-holdings", result.StdOut);
        Assert.DoesNotContain("Skipping plugin", result.StdErr);
    }

    [Fact]
    public async Task PluginsList_PicksUpNewFolderWithoutRebuild()
    {
        using var temp = new TempDirectory();
        CopyDirectory(HelperPaths.GetPluginFixturesDirectory(), temp.Path);
        var second = Directory.CreateDirectory(Path.Combine(temp.Path, "second")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(second, "plugin.json"),
            """{ "name": "second", "version": "0.2.0", "capabilities": ["download-transactions"] }""",
            TestContext.Current.CancellationToken);

        var result = await CliRunner.RunAsync(["plugins", "list"], PluginDirectoryEnv(temp.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dummy", result.StdOut);
        Assert.Contains("second", result.StdOut);
        Assert.Contains("0.2.0", result.StdOut);
        Assert.True(
            result.StdOut.IndexOf("dummy", StringComparison.Ordinal) < result.StdOut.IndexOf("second", StringComparison.Ordinal),
            "dummy should be listed before second.");
    }

    [Fact]
    public async Task PluginsList_EmptyDirectory_ReportsNoPlugins()
    {
        using var temp = new TempDirectory();

        var result = await CliRunner.RunAsync(["plugins", "list"], PluginDirectoryEnv(temp.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No plugins installed.", result.StdOut);
    }

    [Fact]
    public async Task PluginsList_InvalidPlugin_WarnsAndStillSucceeds()
    {
        using var temp = new TempDirectory();
        CopyDirectory(HelperPaths.GetPluginFixturesDirectory(), temp.Path);
        var broken = Directory.CreateDirectory(Path.Combine(temp.Path, "broken")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(broken, "plugin.json"),
            """{ "name": """,
            TestContext.Current.CancellationToken);

        var result = await CliRunner.RunAsync(["plugins", "list"], PluginDirectoryEnv(temp.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dummy", result.StdOut);
        Assert.DoesNotContain("broken", result.StdOut);
        Assert.Contains("Skipping plugin", result.StdErr);
    }

    [Fact]
    public async Task Plugins_MissingSubcommand_PrintsUsageAndReturnsOne()
    {
        var result = await CliRunner.RunAsync(["plugins"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage", result.StdErr);
    }

    [Fact]
    public async Task Plugins_UnknownSubcommand_PrintsUsageAndReturnsOne()
    {
        var result = await CliRunner.RunAsync(["plugins", "bogus"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage", result.StdErr);
    }

    [Fact]
    public async Task PluginsList_MissingDirectory_ReportsNoPlugins()
    {
        var missing = Path.Combine(Path.GetTempPath(), "flushedyo-missing-" + Guid.NewGuid().ToString("N"));

        var result = await CliRunner.RunAsync(["plugins", "list"], PluginDirectoryEnv(missing));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No plugins installed.", result.StdOut);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flushedyo-cli-plugins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
