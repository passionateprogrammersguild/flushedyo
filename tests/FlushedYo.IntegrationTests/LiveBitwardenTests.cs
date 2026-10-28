using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.IntegrationTests;

[Collection("EnvironmentMutation")]
[Trait("Category", "Live")]
public sealed class LiveBitwardenTests
{
    private const string FixtureItemKey = "testconnection";

    private static BitwardenMcpSecretStore CreateLiveStore(string session = "${BW_SESSION}") =>
        StoreFactory.Create(new BitwardenMcpOptions
        {
            Command = "npx",
            Arguments = ["-y", "@bitwarden/mcp-server"],
            ConnectTimeoutSeconds = 60,
            CallTimeoutSeconds = 30,
            EnvironmentVariables = new Dictionary<string, string> { ["BW_SESSION"] = session },
        });

    private static CancellationToken Token() => new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token;

    [LiveBitwardenFact]
    public async Task GetSecret_LiveHappyPath_ReturnsValue()
    {
        LiveBitwardenFactAttribute.RequireSession();

        // Fail fast on a locked vault or missing fixture item instead of waiting for the MCP
        // server to start and report the same condition.
        await BitwardenCli.RequireLoginItemAsync(FixtureItemKey);

        var secret = await CreateLiveStore().GetSecretAsync(FixtureItemKey, Token());

        if (secret is null)
        {
            Assert.Fail("Bitwarden item 'testconnection' not found. Create a Login item named 'testconnection' with a password in the vault used by BW_SESSION.");
        }

        Assert.True(!string.IsNullOrWhiteSpace(secret), "Secret was empty.");
    }

    [LiveBitwardenFact]
    public async Task GetSecret_LiveNotFound_ReturnsNull()
    {
        LiveBitwardenFactAttribute.RequireSession();

        var secret = await CreateLiveStore().GetSecretAsync($"flushedyo-nonexistent-{Guid.NewGuid()}", Token());

        Assert.Null(secret);
    }

    [LiveBitwardenFact]
    public async Task GetSecret_LiveInvalidSession_ThrowsUnlockHint()
    {
        var ex = await Assert.ThrowsAsync<SecretStoreException>(
            () => CreateLiveStore("invalid-token-123").GetSecretAsync(FixtureItemKey, Token()));

        Assert.Contains("unlock", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
