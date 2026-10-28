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

        var loaded = new List<PluginDescriptor>();
        var folders = Directory.EnumerateDirectories(root)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folder);
            if (folderName.StartsWith('.'))
            {
                continue;
            }

            var descriptor = TryLoad(folder, folderName, out var failure);
            if (descriptor is null)
            {
                LogSkipped(logger, folderName, failure!);
                continue;
            }

            loaded.Add(descriptor);
        }

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

        survivors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return Task.FromResult<IReadOnlyList<PluginDescriptor>>(survivors);
    }

    private static PluginDescriptor? TryLoad(string folder, string folderName, out string? failure)
    {
        var manifestPath = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            failure = $"'{manifestPath}' not found.";
            return null;
        }

        try
        {
            var manifest = PluginFileParser.ParseManifest(File.ReadAllText(manifestPath), manifestPath);

            if (!string.Equals(folderName, manifest.Name, StringComparison.OrdinalIgnoreCase))
            {
                failure = $"folder name '{folderName}' does not match manifest name '{manifest.Name}'.";
                return null;
            }

            var configuration = PluginConfiguration.Default;
            var configurationPath = Path.Combine(folder, ConfigurationFileName);
            if (File.Exists(configurationPath))
            {
                configuration = PluginFileParser.ParseConfiguration(File.ReadAllText(configurationPath), configurationPath);
            }

            failure = FindMissingEntry(folder, "skills", manifest.Skills)
                ?? FindMissingEntry(folder, "tools", manifest.Tools)
                ?? FindMissingAssembly(folder, manifest);
            return failure is null ? new PluginDescriptor(manifest, configuration, Path.GetFullPath(folder)) : null;
        }
        catch (PluginFileException ex)
        {
            failure = ex.Message;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = $"could not read plugin files: {ex.Message}";
            return null;
        }
    }

    // Discovery only checks the file exists; it never loads or inspects the assembly.
    private static string? FindMissingAssembly(string folder, PluginManifest manifest)
    {
        if (manifest.Assembly is null)
        {
            return null;
        }

        var folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var assemblyPath = Path.GetFullPath(Path.Combine(folderPath, manifest.Assembly));

        // The parser already rejects '..'; this is defence in depth.
        if (!assemblyPath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return $"assembly '{manifest.Assembly}' is outside the plugin folder.";
        }

        return File.Exists(assemblyPath) ? null : $"assembly '{manifest.Assembly}' not found.";
    }

    private static string? FindMissingEntry(string folder, string subfolder, IReadOnlyList<string> entries)
    {
        foreach (var entry in entries)
        {
            if (entry == ".." || entry.AsSpan().IndexOfAny('/', '\\') >= 0)
            {
                return $"{subfolder} entry '{entry}' must be a plain name, not a path.";
            }

            var basePath = Path.Combine(folder, subfolder, entry);
            if (!File.Exists(basePath) && !Directory.Exists(basePath) && !File.Exists(basePath + ".md"))
            {
                return $"{subfolder} entry '{entry}' not found in '{Path.Combine(folder, subfolder)}'.";
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Plugin directory '{Root}' does not exist; no plugins loaded.")]
    private static partial void LogDirectoryMissing(ILogger logger, string root);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping plugin '{FolderName}': {Reason}")]
    private static partial void LogSkipped(ILogger logger, string folderName, string reason);
}
