using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Focused tests for the preflight locked-status detectors extracted from
/// <see cref="BitwardenMcpSecretStore"/>. Pure statics; no environment mutation, so they run
/// outside the EnvironmentMutation collection.
/// </summary>
public sealed class BitwardenMcpLockedDetectionTests
{
    [Theory]
    [InlineData("""{"status":"locked"}""", "locked")]
    [InlineData("""{"status":"unauthenticated"}""", "unauthenticated")]
    [InlineData("""{"status":"unlocked"}""", null)]
    [InlineData("""{"other":"locked"}""", null)]
    [InlineData("""{}""", null)]
    public void TryDetectLockedFromJson_MatchesOnlyLockedStatuses(string json, string? expected)
        => Assert.Equal(expected, BitwardenMcpSecretStore.TryDetectLockedFromJson(json));

    [Theory]
    [InlineData("\"status\": \"locked\"", "locked")]
    [InlineData("\"STATUS\": \"UNAUTHENTICATED\"", "UNAUTHENTICATED")]
    [InlineData("\"status\": \"unlocked\"", null)]
    [InlineData("no status here", null)]
    public void TryDetectLockedFromRegex_MatchesOnlyLockedStatuses(string text, string? expected)
        => Assert.Equal(expected, BitwardenMcpSecretStore.TryDetectLockedFromRegex(text));

    [Theory]
    [InlineData("not json at all", null)]                // regex finds no status
    [InlineData("{ \"status\": \"locked\" junk", "locked")]  // malformed JSON with locked text
    [InlineData("""{"status":"locked"}""", "locked")]    // well-formed JSON
    public void TryDetectLockedStatus_HandlesMalformedAndHealthyInput(string text, string? expected)
        => Assert.Equal(expected, BitwardenMcpSecretStore.TryDetectLockedStatus(text));

    [Fact]
    public void TryDetectLockedStatus_HealthyJson_WinsOverRegex()
    {
        // The raw text would match the regex fallback ("locked"), but the parsed status is
        // healthy: JSON wins when it parses.
        var text = """{"status":"unlocked","note":"locked appears in prose"}""";

        Assert.Null(BitwardenMcpSecretStore.TryDetectLockedStatus(text));
    }
}
