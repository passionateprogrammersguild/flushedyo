namespace FlushedYo.Core.SecretStore;

/// <summary>Thrown when more than one secret-store item has the requested key.</summary>
/// <param name="key">The duplicated secret key.</param>
public sealed class DuplicateSecretKeyException(string key)
    : SecretStoreException($"Secret key '{key}' exists more than once in the secret store; use a unique item name or id");
