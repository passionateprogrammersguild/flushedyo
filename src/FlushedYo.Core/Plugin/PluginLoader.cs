using FlushedYo.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// Scans the plugin directory and loads every valid plugin folder. Invalid plugins are skipped
/// with a warning. The loader reads JSON and checks files exist; it never touches secrets or MCP servers.
/// </summary>
public sealed partial class PluginLoader(IOptions<FlushedYoOptions> options, ILogger<PluginLoader> logger)
{
    private const string ManifestFileName = "plugin.json";
    private const string ConfigurationFileName = "config.json";

    /// <summary>Loads all valid plugins, sorted by name.</summary>
    /// <param name="cancellationToken">Token that cancels the scan between plugin folders.</param>
    /// <returns>The valid plugins.</returns>
    public Task<IReadOnlyList<PluginDescriptor>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = PluginDirectoryResolver.Resolve(options.Value.PluginDirectory);
        if (!Directory.Exists(root))
        {
            LogDirectoryMissing(logger, root);
            return Task.FromResult<IReadOnlyList<PluginDescriptor>>([]);
        }

        var loaded = LoadAll(root, cancellationToken);
        var survivors = RemoveDuplicateNames(loaded);
        return Task.FromResult<IReadOnlyList<PluginDescriptor>>(SortByName(survivors));
    }

    private List<PluginDescriptor> LoadAll(string root, CancellationToken cancellationToken)
    {
        var loaded = new List<PluginDescriptor>();
        foreach (var folder in EnumeratePluginFolders(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = TryLoad(folder);
            if (result.Descriptor is not { } descriptor)
            {
                LogSkipped(logger, folder.Name, result.Failure!);
                continue;
            }

            loaded.Add(descriptor);
        }

        return loaded;
    }

    private static IEnumerable<PluginFolder> EnumeratePluginFolders(string root) =>
        Directory.EnumerateDirectories(root)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Where(static folder => !Path.GetFileName(folder).StartsWith('.'))
            .Select(static folder => new PluginFolder(folder, Path.GetFileName(folder)));

    private List<PluginDescriptor> RemoveDuplicateNames(List<PluginDescriptor> loaded)
    {
        var duplicateNames = loaded
            .GroupBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var survivors = new List<PluginDescriptor>(loaded.Count);
        foreach (var plugin in loaded)
        {
            if (duplicateNames.Contains(plugin.Name))
            {
                LogSkipped(logger, Path.GetFileName(plugin.DirectoryPath), $"duplicate plugin name '{plugin.Name}'.");
                continue;
            }

            survivors.Add(plugin);
        }

        return survivors;
    }

    private static List<PluginDescriptor> SortByName(List<PluginDescriptor> plugins)
    {
        plugins.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return plugins;
    }

    private static PluginLoadResult TryLoad(PluginFolder folder)
    {
        var manifestPath = Path.Combine(folder.Path, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return PluginLoadResult.Failed($"'{manifestPath}' not found.");
        }

        try
        {
            var manifest = PluginFileParser.ParseManifest(File.ReadAllText(manifestPath), manifestPath);

            if (!string.Equals(folder.Name, manifest.Name, StringComparison.OrdinalIgnoreCase))
            {
                return PluginLoadResult.Failed($"folder name '{folder.Name}' does not match manifest name '{manifest.Name}'.");
            }

            var configuration = PluginConfiguration.Default;
            var configurationPath = Path.Combine(folder.Path, ConfigurationFileName);
            if (File.Exists(configurationPath))
            {
                configuration = PluginFileParser.ParseConfiguration(File.ReadAllText(configurationPath), configurationPath);
            }

            if (FirstMissingRequirement(folder, manifest) is { } failure)
            {
                return PluginLoadResult.Failed(failure);
            }

            var descriptor = new PluginDescriptor(manifest, configuration, Path.GetFullPath(folder.Path));
            return new PluginLoadResult(descriptor, null);
        }
        catch (PluginFileException ex)
        {
            return PluginLoadResult.Failed(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PluginLoadResult.Failed($"could not read plugin files: {ex.Message}");
        }
    }

    // Discovery only checks the file exists; it never loads or inspects the assembly.
    private static string? FindMissingAssembly(PluginFolder folder, PluginManifest manifest)
    {
        if (manifest.Assembly is null)
        {
            return null;
        }

        var folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Path));
        var assemblyPath = Path.GetFullPath(Path.Combine(folderPath, manifest.Assembly));

        // The parser already rejects '..'; this is defence in depth.
        if (!assemblyPath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return $"assembly '{manifest.Assembly}' is outside the plugin folder.";
        }

        return File.Exists(assemblyPath) ? null : $"assembly '{manifest.Assembly}' not found.";
    }

    private static string? FirstMissingRequirement(PluginFolder folder, PluginManifest manifest)
    {
        EntryRequirement[] requirements =
        [
            new("skills", manifest.Skills),
            new("tools", manifest.Tools),
        ];

        foreach (var requirement in requirements)
        {
            if (FindMissingEntry(folder, requirement) is { } missing)
            {
                return missing;
            }
        }

        return FindMissingAssembly(folder, manifest);
    }

    private static string? FindMissingEntry(PluginFolder folder, EntryRequirement requirement)
    {
        foreach (var entry in requirement.Entries)
        {
            if (!IsPlainName(entry))
            {
                return $"{requirement.Subfolder} entry '{entry}' must be a plain name, not a path.";
            }

            var basePath = Path.Combine(folder.Path, requirement.Subfolder, entry);
            if (!EntryExists(basePath))
            {
                return $"{requirement.Subfolder} entry '{entry}' not found in '{Path.Combine(folder.Path, requirement.Subfolder)}'.";
            }
        }

        return null;
    }

    private static bool IsPlainName(string entry) =>
        entry != ".." && entry.AsSpan().IndexOfAny('/', '\\') < 0;

    private static bool EntryExists(string basePath) =>
        File.Exists(basePath) || Directory.Exists(basePath) || File.Exists(basePath + ".md");

    private sealed record PluginFolder(string Path, string Name);

    private sealed record EntryRequirement(string Subfolder, IReadOnlyList<string> Entries);

    private sealed record PluginLoadResult(PluginDescriptor? Descriptor, string? Failure)
    {
        public static PluginLoadResult Failed(string failure) => new(null, failure);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Plugin directory '{Root}' does not exist; no plugins loaded.")]
    private static partial void LogDirectoryMissing(ILogger logger, string root);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping plugin '{FolderName}': {Reason}")]
    private static partial void LogSkipped(ILogger logger, string folderName, string reason);
}
