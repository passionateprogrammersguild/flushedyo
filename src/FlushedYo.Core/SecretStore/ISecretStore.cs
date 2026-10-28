namespace FlushedYo.Core.SecretStore;

/// <summary>
/// Abstraction over a secret store such as Bitwarden (via MCP) or environment variables.
/// Implementations must never log or persist raw secret values.
/// </summary>
public interface ISecretStore
{
    /// <summary>Resolves the secret value for <paramref name="key"/>, or null when not found.</summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Returns true when a secret exists for <paramref name="key"/>.</summary>
    Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default);
}
