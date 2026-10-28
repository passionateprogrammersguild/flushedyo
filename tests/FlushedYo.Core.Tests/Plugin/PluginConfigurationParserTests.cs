using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Validation rules for <c>config.json</c> (spec: plugin_manifest_models).</summary>
public sealed class PluginConfigurationParserTests
{
    private const string SourcePath = "/x/plugins/fidelity/config.json";

    [Fact]
    public void FullExample_ParsesAllFields()
    {
        var configuration = PluginFileParser.ParseConfiguration(
            """
            {
              "bitwardenMcpUrl": "http://localhost:3000",
              "playwrightMcpUrl": "http://localhost:3001",
              "headless": false,
              "credentialKeys": {
                "username": "fidelity/username",
                "password": "fidelity/password"
              }
            }
            """,
            SourcePath);

        Assert.Equal("http://localhost:3000", configuration.BitwardenMcpUrl);
        Assert.Equal("http://localhost:3001", configuration.PlaywrightMcpUrl);
        Assert.False(configuration.Headless);
        Assert.Equal("fidelity/username", configuration.CredentialKeys.Username);
        Assert.Equal("fidelity/password", configuration.CredentialKeys.Password);
    }

    [Fact]
    public void EmptyObject_UsesDefaults()
    {
        var configuration = PluginFileParser.ParseConfiguration("{}", SourcePath);

        Assert.Null(configuration.BitwardenMcpUrl);
        Assert.Null(configuration.PlaywrightMcpUrl);
        Assert.True(configuration.Headless);
        Assert.Null(configuration.CredentialKeys.Username);
        Assert.Null(configuration.CredentialKeys.Password);
    }

    [Fact]
    public void Default_HasHeadlessTrueAndNoUrls()
    {
        Assert.True(PluginConfiguration.Default.Headless);
        Assert.Null(PluginConfiguration.Default.BitwardenMcpUrl);
        Assert.Null(PluginConfiguration.Default.PlaywrightMcpUrl);
    }

    [Fact]
    public void UnknownKeys_AreIgnored()
    {
        var configuration = PluginFileParser.ParseConfiguration(
            """{ "timeoutSeconds": 30, "bitwardenMcpUrl": "http://localhost:3000" }""",
            SourcePath);

        Assert.Equal("http://localhost:3000", configuration.BitwardenMcpUrl);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://example.test:8443/mcp")]
    public void HttpUrl_IsAccepted(string url)
    {
        var configuration = PluginFileParser.ParseConfiguration(
            $$"""{ "bitwardenMcpUrl": "{{url}}" }""",
            SourcePath);

        Assert.Equal(url, configuration.BitwardenMcpUrl);
    }

    [Fact]
    public void FtpScheme_Throws()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseConfiguration(
            """{ "playwrightMcpUrl": "ftp://x" }""",
            SourcePath));
    }

    [Fact]
    public void MissingScheme_Throws()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseConfiguration(
            """{ "bitwardenMcpUrl": "localhost:3000" }""",
            SourcePath));
    }

    [Fact]
    public void EmptyUrl_Throws()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseConfiguration(
            """{ "bitwardenMcpUrl": "" }""",
            SourcePath));
    }

    [Fact]
    public void NonBooleanHeadless_Throws()
    {
        var exception = Assert.Throws<PluginFileException>(() => PluginFileParser.ParseConfiguration(
            """{ "headless": "yes" }""",
            SourcePath));

        Assert.Contains(SourcePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJson_ThrowsWithSourcePath()
    {
        var exception = Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseConfiguration("""{ "bitwardenMcpUrl": """, SourcePath));

        Assert.Contains(SourcePath, exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }
}
