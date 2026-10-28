using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Table rendering for <c>plugins list</c> (spec: plugins_list_command).</summary>
public sealed class PluginTableFormatterTests
{
    [Fact]
    public void EmptyList_RendersNoPluginsMessage()
    {
        Assert.Equal("No plugins installed.", PluginTableFormatter.Format([]));
    }

    [Fact]
    public void SinglePlugin_RendersHeaderAndRow()
    {
        var output = PluginTableFormatter.Format([Plugin("fidelity", "1.0.0", PluginCapability.DownloadTransactions)]);

        Assert.Equal(
            "NAME      VERSION  CAPABILITIES\n" +
            "fidelity  1.0.0    download-transactions",
            output);
    }

    [Fact]
    public void ColumnWidths_FollowLongestCell()
    {
        var output = PluginTableFormatter.Format(
        [
            Plugin("fidelity", "1.0.0"),
            Plugin("a", "10.20.30-beta.1"),
        ]);

        Assert.Equal(
            "NAME      VERSION          CAPABILITIES\n" +
            "fidelity  1.0.0            -\n" +
            "a         10.20.30-beta.1  -",
            output);
    }

    [Fact]
    public void MultiplePlugins_KeepInputOrder()
    {
        var output = PluginTableFormatter.Format([Plugin("zeta", "1.0.0"), Plugin("alpha", "1.0.0")]);

        var lines = output.Split('\n');
        Assert.StartsWith("zeta", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("alpha", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void NoCapabilities_RendersDash()
    {
        var output = PluginTableFormatter.Format([Plugin("schwab", "0.1.0")]);

        Assert.EndsWith("  -", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BothCapabilities_AreCommaSeparated()
    {
        var output = PluginTableFormatter.Format(
            [Plugin("fidelity", "1.0.0", PluginCapability.DownloadTransactions, PluginCapability.DownloadHoldings)]);

        Assert.EndsWith("download-transactions, download-holdings", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_HasNoTrailingNewlineTrailingSpacesOrCarriageReturns()
    {
        var output = PluginTableFormatter.Format(
        [
            Plugin("fidelity", "1.0.0", PluginCapability.DownloadHoldings),
            Plugin("schwab", "0.1.0"),
        ]);

        Assert.False(output.EndsWith('\n'));
        Assert.DoesNotContain('\r', output);
        foreach (var line in output.Split('\n'))
        {
            Assert.Equal(line.TrimEnd(), line);
        }
    }

    private static PluginDescriptor Plugin(string name, string version, params PluginCapability[] capabilities) =>
        new(
            new PluginManifest { Name = name, Version = version, Capabilities = capabilities },
            PluginConfiguration.Default,
            "/plugins/" + name);
}
