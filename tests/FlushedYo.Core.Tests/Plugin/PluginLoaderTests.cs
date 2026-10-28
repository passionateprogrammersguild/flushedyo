using FlushedYo.Core.Configuration;
using FlushedYo.Core.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Directory scanning and validation (spec: plugin_discovery_loader).</summary>
public sealed class PluginLoaderTests
{
    private static PluginLoader CreateLoader(string root, CapturingLogger<PluginLoader> logger) =>
        new(Options.Create(new FlushedYoOptions { PluginDirectory = root }), logger);

    [Fact]
    public async Task MissingDirectory_YieldsEmptyListWithInformationLog()
    {
        var logger = new CapturingLogger<PluginLoader>();
        var loader = CreateLoader(Path.Combine(Path.GetTempPath(), "flushedyo-missing-" + Guid.NewGuid().ToString("N")), logger);

        var plugins = await loader.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
    }

    [Fact]
    public async Task ManifestOnly_LoadsWithDefaultConfiguration()
    {
        using var temp = new TempPluginDirectory();
        var folder = temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        var plugin = Assert.Single(plugins);
        Assert.Equal("fidelity", plugin.Name);
        Assert.True(plugin.Configuration.Headless);
        Assert.Equal(Path.GetFullPath(folder), plugin.DirectoryPath);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ConfigFile_IsParsedIntoDescriptor()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(
            "fidelity",
            TempPluginDirectory.Manifest("fidelity"),
            """{ "headless": false, "credentialKeys": { "username": "fidelity/username" } }""");

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        var plugin = Assert.Single(plugins);
        Assert.False(plugin.Configuration.Headless);
        Assert.Equal("fidelity/username", plugin.Configuration.CredentialKeys.Username);
    }

    [Fact]
    public async Task MissingManifest_IsSkippedWithWarning_OthersStillLoad()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("empty", manifestJson: null);
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fidelity", Assert.Single(plugins).Name);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("empty", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedManifest_IsSkippedWithWarningNamingFile()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("broken", """{ "name": """);
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("plugin.json", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedConfig_IsSkippedWithWarning()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"), """{ "headless": "yes" }""");
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        Assert.Contains("config.json", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FolderNameDiffersOnlyByCase_IsLoaded()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("Fidelity", TempPluginDirectory.Manifest("fidelity"));

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fidelity", Assert.Single(plugins).Name);
    }

    [Fact]
    public async Task FolderNameMismatch_IsSkippedWithWarning()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("other", TempPluginDirectory.Manifest("fidelity"));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task SkillFileWithMarkdownExtension_IsResolved()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(
            "fidelity",
            TempPluginDirectory.Manifest("fidelity", extra: """, "skills": ["login"]"""),
            skillFiles: ["login.md"]);

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Single(plugins);
    }

    [Fact]
    public async Task MissingSkill_IsSkippedWithWarningNamingEntry()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity", extra: """, "skills": ["login"]"""));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        Assert.Contains("login", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeclaredAssembly_WhenPresent_IsLoadedAndRoundTrips()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(
            "fidelity",
            TempPluginDirectory.Manifest("fidelity", extra: """, "assembly": "Stub.dll", "type": "Company.Plugin.MyPlugin" """),
            assemblyFiles: ["Stub.dll"]);
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        var plugin = Assert.Single(plugins);
        Assert.Equal("Stub.dll", plugin.Manifest.Assembly);
        Assert.Equal("Company.Plugin.MyPlugin", plugin.Manifest.Type);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DeclaredAssembly_WhenMissing_IsSkippedWithWarning_OthersStillLoad()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(
            "broken",
            TempPluginDirectory.Manifest("broken", extra: """, "assembly": "Missing.dll", "type": "A.B" """));
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fidelity", Assert.Single(plugins).Name);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Missing.dll", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeclaredAssembly_InSubfolder_IsLoaded()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(
            "fidelity",
            TempPluginDirectory.Manifest("fidelity", extra: """, "assembly": "bin/Stub.dll", "type": "A.B" """),
            assemblyFiles: ["bin/Stub.dll"]);

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("bin/Stub.dll", Assert.Single(plugins).Manifest.Assembly);
    }

    [Fact]
    public async Task PluginWithoutAssembly_IsUnaffected()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        var plugin = Assert.Single(plugins);
        Assert.Null(plugin.Manifest.Assembly);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DeclaredAssembly_IsNeverInspected_AnEmptyFileIsAccepted()
    {
        using var temp = new TempPluginDirectory();
        var folder = temp.AddPlugin(
            "fidelity",
            TempPluginDirectory.Manifest("fidelity", extra: """, "assembly": "Stub.dll", "type": "A.B" """),
            assemblyFiles: ["Stub.dll"]);

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, new FileInfo(Path.Combine(folder, "Stub.dll")).Length);
        Assert.Single(plugins);
    }

    [Fact]
    public async Task ToolDirectory_IsResolved()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(
            "fidelity",
            TempPluginDirectory.Manifest("fidelity", extra: """, "tools": ["click"]"""),
            toolDirectories: ["click"]);

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Single(plugins);
    }

    [Fact]
    public async Task SkillEntryWithPathTraversal_IsSkippedWithWarning()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity", extra: """, "skills": ["../secret"]"""));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task DuplicateNames_ExcludeEveryCopyWithWarningEach()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        Assert.SkipWhen(
            Directory.Exists(Path.Combine(temp.Root, "Fidelity")),
            "Two folders differing only by case need a case-sensitive file system.");
        temp.AddPlugin("Fidelity", TempPluginDirectory.Manifest("fidelity"));
        temp.AddPlugin("schwab", TempPluginDirectory.Manifest("schwab"));
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("schwab", Assert.Single(plugins).Name);
        Assert.Equal(
            2,
            logger.Entries.Count(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("duplicate", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task HiddenFolder_IsIgnoredSilently()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin(".git", manifestJson: null);
        var logger = new CapturingLogger<PluginLoader>();

        var plugins = await CreateLoader(temp.Root, logger).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(plugins);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Results_AreSortedByName()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("schwab", TempPluginDirectory.Manifest("schwab"));
        temp.AddPlugin("alpha", TempPluginDirectory.Manifest("alpha"));
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));

        var plugins = await CreateLoader(temp.Root, new CapturingLogger<PluginLoader>())
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["alpha", "fidelity", "schwab"], plugins.Select(static plugin => plugin.Name).ToArray());
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        using var temp = new TempPluginDirectory();
        temp.AddPlugin("fidelity", TempPluginDirectory.Manifest("fidelity"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateLoader(temp.Root, new CapturingLogger<PluginLoader>()).LoadAsync(cts.Token));
    }
}
