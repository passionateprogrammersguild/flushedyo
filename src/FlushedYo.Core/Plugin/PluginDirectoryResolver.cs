using FlushedYo.Core.Configuration;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// Resolves the configured plugin directory to an absolute path.
/// </summary>
public static class PluginDirectoryResolver
{
    /// <summary>
    /// Resolves <paramref name="configuredPath"/>. Absolute paths are used as-is; relative paths are
    /// resolved against the repository root, found by walking up from the current directory.
    /// </summary>
    /// <param name="configuredPath">The <c>FlushedYo:PluginDirectory</c> value.</param>
    /// <returns>An absolute path. The directory is not required to exist.</returns>
    /// <exception cref="ArgumentException"><paramref name="configuredPath"/> is null or whitespace.</exception>
    public static string Resolve(string configuredPath) =>
        RepositoryPathResolver.Resolve(configuredPath);

    internal static string Resolve(string configuredPath, string startDirectory) =>
        RepositoryPathResolver.Resolve(configuredPath, startDirectory);
}
