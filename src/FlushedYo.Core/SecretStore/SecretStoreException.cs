namespace FlushedYo.Core.SecretStore;

/// <summary>Thrown when a secret store fails to resolve a secret (e.g. an MCP tool error).</summary>
public sealed class SecretStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
