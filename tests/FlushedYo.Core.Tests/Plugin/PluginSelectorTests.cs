using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Choosing plugins by <c>--plugin</c> (spec: cli_shared_building_blocks).</summary>
public sealed class PluginSelectorTests
{
    private static PluginDescriptor Descriptor(string name) =>
        new(new PluginManifest { Name = name, Version = "1.0.0" }, PluginConfiguration.Default, "/plugins/" + name);

    [Fact]
    public void NoName_SelectsEveryPlugin_InOrder()
    {
        PluginDescriptor[] installed = [Descriptor("alpha"), Descriptor("bravo")];

        var selection = PluginSelector.Select(installed, null);

        Assert.Null(selection.Error);
        Assert.Equal(installed, selection.Plugins);
    }

    [Fact]
    public void Name_MatchesCaseInsensitively_AndSelectsOnlyThatPlugin()
    {
        var selection = PluginSelector.Select([Descriptor("alpha"), Descriptor("fidelity")], "FIDELITY");

        Assert.Null(selection.Error);
        Assert.Equal("fidelity", Assert.Single(selection.Plugins).Name);
    }

    [Fact]
    public void UnknownName_ListsTheInstalledPlugins()
    {
        var selection = PluginSelector.Select([Descriptor("alpha"), Descriptor("bravo")], "nope");

        Assert.Empty(selection.Plugins);
        Assert.Equal("Plugin 'nope' was not found. Installed plugins: alpha, bravo.", selection.Error);
    }

    [Fact]
    public void UnknownName_WithNothingInstalled_SaysSo()
    {
        var selection = PluginSelector.Select([], "nope");

        Assert.Empty(selection.Plugins);
        Assert.Equal("Plugin 'nope' was not found. No plugins are installed.", selection.Error);
    }

    [Fact]
    public void NoName_WithNothingInstalled_IsEmptyWithoutError()
    {
        var selection = PluginSelector.Select([], null);

        Assert.Empty(selection.Plugins);
        Assert.Null(selection.Error);
    }
}
