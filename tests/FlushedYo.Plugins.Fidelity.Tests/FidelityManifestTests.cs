using FlushedYo.Core.Configuration;
using FlushedYo.Core.Plugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlushedYo.Plugins.Fidelity.Tests;

public sealed class FidelityManifestTests
{
    [Fact]
    public async Task RealPluginFolderIsDiscoverableByPluginLoader()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "flushedyo-fidelity-manifest-" + Guid.NewGuid().ToString("N"));
        var pluginFolder = Path.Combine(root, "fidelity");
        Directory.CreateDirectory(pluginFolder);
        try
        {
            CopyTree(RepositoryPluginsFolder(), pluginFolder);

            var loader = new PluginLoader(
                Microsoft.Extensions.Options.Options.Create(new FlushedYoOptions { PluginDirectory = root }),
                NullLogger<PluginLoader>.Instance);

            var plugins = await loader.LoadAsync(TestContext.Current.CancellationToken);

            var plugin = Assert.Single(plugins);
            Assert.Equal("fidelity", plugin.Name);
            Assert.Equal("1.0.0", plugin.Version);
            Assert.Equal([PluginCapability.DownloadTransactions], plugin.Capabilities);
            Assert.Null(plugin.Manifest.Assembly);
            Assert.Null(plugin.Manifest.Type);
            Assert.Equal("fidelity/username", plugin.Configuration.CredentialKeys.Username);
            Assert.Equal("fidelity/password", plugin.Configuration.CredentialKeys.Password);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManifestDeclaresOnlyRealSkillAndToolFiles()
    {
        var folder = RepositoryPluginsFolder();
        var manifest = PluginFileParser.ParseManifest(
            File.ReadAllText(Path.Combine(folder, "plugin.json")),
            "plugin.json");

        Assert.All(manifest.Skills, skill =>
            Assert.True(
                File.Exists(Path.Combine(folder, "skills", skill + ".md")) ||
                File.Exists(Path.Combine(folder, "skills", skill)),
                $"Missing skill file for '{skill}'."));
        Assert.All(manifest.Tools, tool =>
            Assert.True(
                File.Exists(Path.Combine(folder, "tools", tool + ".md")) ||
                File.Exists(Path.Combine(folder, "tools", tool)),
                $"Missing tool file for '{tool}'."));
    }

    private static string RepositoryPluginsFolder() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "fidelity");

    private static void CopyTree(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

