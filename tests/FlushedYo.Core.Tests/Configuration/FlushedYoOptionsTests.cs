using FlushedYo.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace FlushedYo.Core.Tests.Configuration;

/// <summary>
/// Configuration binding. Joins the environment-mutation collection because the override test
/// writes process-wide variables.
/// </summary>
[Collection("EnvironmentMutation")]
public sealed class FlushedYoOptionsTests
{
    [Fact]
    public void Binds_FromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlushedYo:SecretStore:Provider"] = "EnvironmentVariables",
                ["FlushedYo:PluginDirectory"] = "custom-plugins",
                ["FlushedYo:LedgerPath"] = "custom.db",
            })
            .Build();

        var options = configuration.GetSection(FlushedYoOptions.SectionName).Get<FlushedYoOptions>();

        Assert.NotNull(options);
        Assert.Equal("EnvironmentVariables", options.SecretStore.Provider);
        Assert.Equal("custom-plugins", options.PluginDirectory);
        Assert.Equal("custom.db", options.LedgerPath);
    }

    [Fact]
    public void EnvironmentVariables_OverrideConfiguredValues()
    {
        Environment.SetEnvironmentVariable("FLUSHEDYO__LEDGERPATH", "env-override.db");
        Environment.SetEnvironmentVariable("FLUSHEDYO__SECRETSTORE__PROVIDER", "EnvironmentVariables");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FlushedYo:LedgerPath"] = "file-ledger.db",
                    ["FlushedYo:SecretStore:Provider"] = "BitwardenMcp",
                })
                .AddEnvironmentVariables()
                .Build();

            var options = configuration.GetSection(FlushedYoOptions.SectionName).Get<FlushedYoOptions>();

            Assert.NotNull(options);
            Assert.Equal("env-override.db", options.LedgerPath);
            Assert.Equal("EnvironmentVariables", options.SecretStore.Provider);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLUSHEDYO__LEDGERPATH", null);
            Environment.SetEnvironmentVariable("FLUSHEDYO__SECRETSTORE__PROVIDER", null);
        }
    }

    [Fact]
    public void Arguments_OverriddenFromConfiguration_YieldsExactlyConfiguredList()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlushedYo:SecretStore:BitwardenMcp:Arguments:0"] = "-y",
                ["FlushedYo:SecretStore:BitwardenMcp:Arguments:1"] = "custom-server",
            })
            .Build();

        var options = configuration.GetSection(FlushedYoOptions.SectionName).Get<FlushedYoOptions>();

        Assert.NotNull(options);
        Assert.Equal(["-y", "custom-server"], options.SecretStore.BitwardenMcp.Arguments);
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new FlushedYoOptions();

        Assert.Equal(SecretStoreProviders.BitwardenMcp, options.SecretStore.Provider);
        Assert.Equal("plugins", options.PluginDirectory);
        Assert.Equal("data/flushedyo.db", options.LedgerPath);

        var bitwarden = options.SecretStore.BitwardenMcp;
        Assert.Null(bitwarden.Command);
        Assert.Null(bitwarden.Url);
        Assert.Empty(bitwarden.Arguments);
        Assert.Equal("get", bitwarden.GetSecretTool);
        Assert.Equal("id", bitwarden.SecretKeyArgument);
        Assert.Equal("password", bitwarden.ToolArguments["object"]);
        Assert.Equal("${BW_SESSION}", bitwarden.EnvironmentVariables["BW_SESSION"]);
    }
}
