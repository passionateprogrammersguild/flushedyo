namespace FlushedYo.Core.Configuration;

/// <summary>
/// Resolves configured paths (plugin directory, ledger path) to absolute paths. Relative paths
/// resolve against the repository root so results do not depend on where the CLI is launched.
/// </summary>
public static class RepositoryPathResolver
{
    private const string RepositoryMarker = "flushedyo.slnx";

    /// <summary>
    /// Resolves <paramref name="configuredPath"/>. Absolute paths are used as-is; relative paths are
    /// resolved against the repository root, found by walking up from the current directory.
    /// </summary>
    /// <param name="configuredPath">The configured path.</param>
    /// <returns>An absolute path. The path is not required to exist.</returns>
    /// <exception cref="ArgumentException"><paramref name="configuredPath"/> is null or whitespace.</exception>
    public static string Resolve(string configuredPath) =>
        Resolve(configuredPath, Directory.GetCurrentDirectory());

    internal static string Resolve(string configuredPath, string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var repositoryRoot = FindRepositoryRoot(startDirectory) ?? startDirectory;
        return Path.GetFullPath(Path.Combine(repositoryRoot, configuredPath));
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory)); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
