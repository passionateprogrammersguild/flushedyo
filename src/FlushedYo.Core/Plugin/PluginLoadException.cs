namespace FlushedYo.Core.Plugin;

/// <summary>
/// Thrown when a plugin's assembly cannot be loaded or its type cannot be constructed.
/// </summary>
/// <remarks>
/// Messages are value-free: they may name the plugin, the manifest's assembly and type, and the
/// type name of an underlying exception, but never text from an exception thrown by plugin code.
/// </remarks>
public sealed class PluginLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);
