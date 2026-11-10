using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.SecretStore;

/// <summary>
/// Branch-by-branch tests for <see cref="BitwardenMcpOptionsValidator"/>; each row violates
/// exactly one rule and must throw the message that rule produces.
/// </summary>
public sealed class BitwardenMcpOptionsValidatorTests
{
    [Fact]
    public void Validate_MissingBothTransports_Throws()
    {
        var exception = Assert.Throws<SecretStoreException>(
            () => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions()));

        Assert.Contains("requires either Command", exception.Message);
    }

    [Fact]
    public void Validate_InvalidUrl_Throws()
    {
        var exception = Assert.Throws<SecretStoreException>(
            () => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions { Url = "not-a-url" }));

        Assert.Contains("absolute HTTP or HTTPS URI", exception.Message);
    }

    [Fact]
    public void Validate_NonHttpUrl_Throws()
    {
        var exception = Assert.Throws<SecretStoreException>(
            () => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions { Url = "ftp://example.com" }));

        Assert.Contains("absolute HTTP or HTTPS URI", exception.Message);
    }

    [Fact]
    public void Validate_RelativeUrl_Throws()
    {
        var exception = Assert.Throws<SecretStoreException>(
            () => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions { Url = "/api" }));

        Assert.Contains("absolute HTTP or HTTPS URI", exception.Message);
    }

    [Fact]
    public void Validate_HttpUrl_Passes()
        => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions { Url = "http://127.0.0.1:1" });

    [Fact]
    public void Validate_HttpsUrl_Passes()
        => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions { Url = "https://example.com/mcp" });

    [Fact]
    public void Validate_CommandOnly_Passes()
        => BitwardenMcpOptionsValidator.Validate(new BitwardenMcpOptions
        {
            Command = "npx",
            EnvironmentVariables = [],
        });

    [Fact]
    public void Validate_StdioWithUnexpandedEnvVar_Throws()
    {
        var options = new BitwardenMcpOptions
        {
            Command = "npx",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["BW_SESSION"] = "${FLUSHEDYO_VALIDATOR_TEST_MISSING_VAR}",
            },
        };

        var exception = Assert.Throws<SecretStoreException>(
            () => BitwardenMcpOptionsValidator.Validate(options));

        Assert.Contains("FLUSHEDYO_VALIDATOR_TEST_MISSING_VAR is not set", exception.Message);
    }

    [Fact]
    public void Validate_StdioWithResolvedEnvVar_Passes()
    {
        var name = "FLUSHEDYO_VALIDATOR_TEST_PRESENT_VAR";
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, "value");
        try
        {
            var options = new BitwardenMcpOptions
            {
                Command = "npx",
                EnvironmentVariables = new Dictionary<string, string> { ["BW_SESSION"] = $"${{{name}}}" },
            };

            BitwardenMcpOptionsValidator.Validate(options);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public void Validate_HttpTransport_SkipsEnvironmentVariableCheck()
    {
        // The ${VAR} is unresolved, but URL transports launch no child process, so the
        // environment variables are irrelevant and validation must pass.
        var options = new BitwardenMcpOptions
        {
            Url = "http://127.0.0.1:1",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["BW_SESSION"] = "${FLUSHEDYO_VALIDATOR_TEST_MISSING_VAR}",
            },
        };

        BitwardenMcpOptionsValidator.Validate(options);
    }
}
