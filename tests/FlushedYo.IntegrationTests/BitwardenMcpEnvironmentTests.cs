using FlushedYo.Core.Configuration;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Verifies that configured environment variables are expanded from the current process and reach
/// the child MCP server. Isolated in its own collection because it mutates process environment.
/// </summary>
[Collection("EnvironmentMutation")]
public sealed class BitwardenMcpEnvironmentTests
{
    [Fact]
    public async Task GetSecret_EnvExpansion_ReachesChild()
    {
        var options = new BitwardenMcpOptions
        {
            Command = "dotnet",
            Arguments = [HelperPaths.GetFakeServerDll(), "echo-session"],
            EnvironmentVariables = new Dictionary<string, string> { ["BW_SESSION"] = "${TEST_SESSION_VAR}" },
        };

        Environment.SetEnvironmentVariable("TEST_SESSION_VAR", "secret-token");
        try
        {
            var store = StoreFactory.Create(options);
            Assert.Equal("SESSION_SET", await store.GetSecretAsync("any-key", TestContext.Current.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SESSION_VAR", null);
        }
    }
}
