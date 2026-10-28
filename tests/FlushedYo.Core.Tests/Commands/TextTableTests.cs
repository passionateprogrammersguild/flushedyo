using FlushedYo.Core.Commands;
using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>
/// Golden output and boundary specs (spec: cli_shared_building_blocks).
/// Structural invariants (LF-only line endings, alignment, no trailing newline or spaces) are
/// covered as properties in <c>FlushedYo.Core.PropertyTests.Commands.TextTableProperties</c>.
/// </summary>
public sealed class TextTableTests
{
    [Fact]
    public void HeadersOnly_RendersJustTheHeaderLine()
    {
        Assert.Equal("A  B", TextTable.Render(["A", "B"], []));
    }

    [Fact]
    public void Columns_AreAsWideAsTheirLongestCell_AndTheLastColumnIsUnpadded()
    {
        var output = TextTable.Render(
            ["NAME", "N"],
            [["alpha", "1"], ["b", "22222"]]);

        Assert.Equal(
            "NAME   N\n" +
            "alpha  1\n" +
            "b      22222",
            output);
    }

    [Fact]
    public void RowWithTheWrongCellCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextTable.Render(["A", "B"], [["only-one"]]));
        Assert.Throws<ArgumentException>(() => TextTable.Render(["A", "B"], [["1", "2", "3"]]));
    }

    [Fact]
    public void NullCell_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextTable.Render(["A", "B"], [["1", null!]]));
    }

    [Fact]
    public void RendersTheSameBytesAsPluginTableFormatter()
    {
        PluginDescriptor[] plugins =
        [
            new(
                new PluginManifest
                {
                    Name = "fidelity",
                    Version = "1.0.0",
                    Capabilities = [PluginCapability.DownloadTransactions, PluginCapability.DownloadHoldings],
                },
                PluginConfiguration.Default,
                "/plugins/fidelity"),
            new(new PluginManifest { Name = "schwab", Version = "0.1.0" }, PluginConfiguration.Default, "/plugins/schwab"),
        ];

        var expected = TextTable.Render(
            ["NAME", "VERSION", "CAPABILITIES"],
            [
                ["fidelity", "1.0.0", "download-transactions, download-holdings"],
                ["schwab", "0.1.0", "-"],
            ]);

        Assert.Equal(expected, PluginTableFormatter.Format(plugins));
        Assert.Equal(
            "NAME      VERSION  CAPABILITIES\n" +
            "fidelity  1.0.0    download-transactions, download-holdings\n" +
            "schwab    0.1.0    -",
            expected);
    }
}
