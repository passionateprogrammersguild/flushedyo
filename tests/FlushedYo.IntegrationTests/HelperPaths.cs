namespace FlushedYo.IntegrationTests;

public static class HelperPaths
{
    public static string GetFakeServerDll() => GetPath("helpers/fake-mcp-server/FlushedYo.Tests.FakeMcpServer.dll");
    public static string GetCliDll() => GetPath("helpers/cli/FlushedYo.Cli.dll");

    public static string GetStubPluginDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "helpers", "stub-plugin");
        if (!File.Exists(Path.Combine(path, "FlushedYo.Tests.StubPlugin.dll")))
        {
            throw new FileNotFoundException($"Stub plugin not found in {path}. Run 'dotnet build flushedyo.slnx' first.");
        }

        return path;
    }

    public static string GetPluginFixturesDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins");
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Plugin fixtures not found at {path}. Run 'dotnet build flushedyo.slnx' first.");
        }

        return path;
    }

    private static string GetPath(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Helper binary not found at {path}. Run 'dotnet build flushedyo.slnx' first.");
        }

        return path;
    }
}
