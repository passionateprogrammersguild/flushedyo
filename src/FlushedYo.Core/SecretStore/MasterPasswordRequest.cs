namespace FlushedYo.Core.SecretStore;

/// <summary>Selects the explicit user-approved source of a master password.</summary>
public enum MasterPasswordSource
{
    /// <summary>Read an existing item from the operating system keystore.</summary>
    OsKeyStore,

    /// <summary>Ask the operating system for a hidden, user-entered password.</summary>
    NativeMaskedPrompt,
}

/// <summary>
/// Describes one master-password read. No environment-variable or command-line password source is
/// supported. Keystore identifiers are not secrets; the returned password must remain in memory.
/// </summary>
public sealed record MasterPasswordRequest
{
    /// <summary>Gets the explicit backend to use.</summary>
    public required MasterPasswordSource Source { get; init; }

    /// <summary>Gets the keystore service name when <see cref="Source"/> is <see cref="MasterPasswordSource.OsKeyStore"/>.</summary>
    public string? Service { get; init; }

    /// <summary>Gets the keystore account name when <see cref="Source"/> is <see cref="MasterPasswordSource.OsKeyStore"/>.</summary>
    public string? Account { get; init; }

    /// <summary>Gets the maximum time to wait for the backend or the user.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}
