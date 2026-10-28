namespace FlushedYo.Core.Plugin;

/// <summary>
/// Thrown when a plugin's <c>plugin.json</c> or <c>config.json</c> file is malformed or fails validation.
/// </summary>
public sealed class PluginFileException(string message, string sourcePath, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Path of the file that failed validation.</summary>
    public string SourcePath { get; } = sourcePath;
}
