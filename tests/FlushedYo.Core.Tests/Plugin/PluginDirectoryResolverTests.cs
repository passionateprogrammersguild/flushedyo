using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Plugin directory resolution (spec: plugin_discovery_loader).</summary>
public sealed class PluginDirectoryResolverTests
{
    [Fact]
    public void AbsolutePath_IsReturnedUnchanged()
    {
        using var temp = new TempPluginDirectory();

        var resolved = PluginDirectoryResolver.Resolve(temp.Root, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(temp.Root), resolved);
    }

    [Fact]
    public void RelativePath_ResolvesAgainstNearestSolutionAncestor()
    {
        using var temp = new TempPluginDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "flushedyo.slnx"), "<Solution />");
        var nested = Directory.CreateDirectory(Path.Combine(temp.Root, "a", "b")).FullName;

        var resolved = PluginDirectoryResolver.Resolve("plugins", nested);

        Assert.Equal(Path.Combine(Path.GetFullPath(temp.Root), "plugins"), resolved);
    }

    [Fact]
    public void RelativePath_WithoutSolutionAncestor_FallsBackToStartDirectory()
    {
        using var temp = new TempPluginDirectory();
        var child = Directory.CreateDirectory(Path.Combine(temp.Root, "child")).FullName;

        var resolved = PluginDirectoryResolver.Resolve("plugins", child);

        Assert.Equal(Path.Combine(child, "plugins"), resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPath_Throws(string configuredPath)
    {
        Assert.Throws<ArgumentException>(() => PluginDirectoryResolver.Resolve(configuredPath, Path.GetTempPath()));
    }

    [Fact]
    public void ResolvedPath_NeedNotExist()
    {
        using var temp = new TempPluginDirectory();

        var resolved = PluginDirectoryResolver.Resolve("does-not-exist", temp.Root);

        Assert.False(Directory.Exists(resolved));
    }
}
