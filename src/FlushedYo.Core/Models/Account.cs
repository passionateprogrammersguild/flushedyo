namespace FlushedYo.Core.Models;

/// <summary>
/// A stable, institution-scoped account identity. <see cref="AccountKey"/> is an opaque identifier
/// supplied by the institution or plugin; it is not an account number and must not contain credentials.
/// </summary>
/// <remarks>
/// Institution names are trimmed and compared case-insensitively. Account keys are trimmed and compared
/// case-sensitively. The pair (institution, account key), rather than either value alone, identifies an account.
/// </remarks>
public sealed record Account
{
    /// <summary>Institution key that owns the account, for example <c>fidelity</c>.</summary>
    public required string Institution { get; init; }

    /// <summary>
    /// Stable opaque account identifier, unique within <see cref="Institution"/>. After trimming, at most
    /// 256 UTF-16 code units are accepted; control characters are rejected to keep identifiers log-safe.
    /// </summary>
    public required string AccountKey { get; init; }
}
