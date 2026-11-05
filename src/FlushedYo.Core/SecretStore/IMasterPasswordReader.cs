namespace FlushedYo.Core.SecretStore;

/// <summary>
/// Reads a master password only after the caller explicitly selects a secure native source.
/// Implementations never log or persist the returned value.
/// </summary>
public interface IMasterPasswordReader
{
    /// <summary>
    /// Reads a master password into memory. The caller must not log, return to a model, or persist
    /// the value. Cancellation and backend failure never fall back to an insecure source.
    /// </summary>
    Task<string> ReadAsync(MasterPasswordRequest request, CancellationToken cancellationToken = default);
}
