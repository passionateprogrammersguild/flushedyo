using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Option validation performed in the constructor, including <c>${VAR}</c> expansion and the HTTP
/// transport that needs no <c>BW_SESSION</c>. Serialized with the other environment-mutating tests
/// because <see cref="Environment.SetEnvironmentVariable"/> is process-wide.
/// </summary>
[Collection("EnvironmentMutation")]
public sealed class BitwardenMcpSecretStoreValidationTests
{
    private const string ExpandVariableName = "FLUSHEDYO_TEST_EXPAND_ME";

    [Fact]
    public void Constructor_Throws_WhenNoTransportConfigured()
    {
        var options = new BitwardenMcpOptions();

        var exception = Assert.Throws<SecretStoreException>(
            () => new BitwardenMcpSecretStore(options, NullLogger<BitwardenMcpSecretStore>.Instance));

        Assert.Contains("requires either Command", exception.Message);
    }

    [Fact]
    public void Constructor_Throws_WhenUrlIsInvalid()
    {
        var options = new BitwardenMcpOptions { Url = "not-a-url" };
        Assert.Throws<SecretStoreException>(() => new BitwardenMcpSecretStore(options, NullLogger<BitwardenMcpSecretStore>.Instance));
    }

    [Fact]
    public void Constructor_Throws_WhenEnvVarUnset()
    {
        Environment.SetEnvironmentVariable(ExpandVariableName, null);
        var options = new BitwardenMcpOptions
        {
            Command = "npx",
            EnvironmentVariables = new Dictionary<string, string> { ["TEST"] = $"${{{ExpandVariableName}}}" }
        };

        var exception = Assert.Throws<SecretStoreException>(() => new BitwardenMcpSecretStore(options, NullLogger<BitwardenMcpSecretStore>.Instance));
        Assert.Contains(ExpandVariableName, exception.Message);
    }

    [Fact]
    public void ExpandEnvironmentVariables_ExpandsAndKeepsUnresolved()
    {
        Environment.SetEnvironmentVariable(ExpandVariableName, "session-token");
        try
        {
            var configured = new Dictionary<string, string>
            {
                ["BW_SESSION"] = $"${{{ExpandVariableName}}}",
                ["BW_OTHER"] = "${FLUSHEDYO_NOT_SET_ANYWHERE}",
                ["BW_PLAIN"] = "literal",
            };

            var expanded = BitwardenMcpSecretStore.ExpandEnvironmentVariables(configured);

            Assert.Equal("session-token", expanded["BW_SESSION"]);
            Assert.Equal("${FLUSHEDYO_NOT_SET_ANYWHERE}", expanded["BW_OTHER"]);
            Assert.Equal("literal", expanded["BW_PLAIN"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExpandVariableName, null);
        }
    }

    [Fact]
    public void HttpTransport_DoesNotRequireBwSession()
    {
        var previous = Environment.GetEnvironmentVariable("BW_SESSION");
        Environment.SetEnvironmentVariable("BW_SESSION", null);
        try
        {
            var options = new BitwardenMcpOptions { Url = "http://127.0.0.1:1" };
            var store = new BitwardenMcpSecretStore(options, NullLogger<BitwardenMcpSecretStore>.Instance);
            Assert.NotNull(store);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BW_SESSION", previous);
        }
    }
}
