using FlushedYo.Core.Configuration;
using FlushedYo.Core.Tests.Plugin;

namespace FlushedYo.Core.Tests.Configuration;

/// <summary>Shared repo-root path resolution (spec: ledger_database_and_migrations).</summary>
public sealed class RepositoryPathResolverTests
{
    [Fact]
    public void AbsolutePath_IsReturnedUnchanged()
    {
        using var temp = new TempPluginDirectory();

        var resolved = RepositoryPathResolver.Resolve(temp.Root, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(temp.Root), resolved);
    }

    [Fact]
    public void RelativePath_ResolvesAgainstNearestSolutionAncestor()
    {
        using var temp = new TempPluginDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "flushedyo.slnx"), "<Solution />");
        var nested = Directory.CreateDirectory(Path.Combine(temp.Root, "a", "b")).FullName;

        var resolved = RepositoryPathResolver.Resolve("data/flushedyo.db", nested);

        Assert.Equal(Path.Combine(Path.GetFullPath(temp.Root), "data", "flushedyo.db"), resolved);
    }

    [Fact]
    public void RelativePath_WithoutSolutionAncestor_FallsBackToStartDirectory()
    {
        using var temp = new TempPluginDirectory();
        var child = Directory.CreateDirectory(Path.Combine(temp.Root, "child")).FullName;

        var resolved = RepositoryPathResolver.Resolve("data/flushedyo.db", child);

        Assert.Equal(Path.Combine(child, "data", "flushedyo.db"), resolved);
    }
}
