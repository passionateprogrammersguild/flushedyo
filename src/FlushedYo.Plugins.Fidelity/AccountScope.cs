namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// The account filter applied while mapping an export: either every row is accepted or only rows
/// whose masked account key equals the one being imported. Comparison is a case-sensitive ordinal
/// match, because account keys are generated hex digests.
/// </summary>
internal readonly record struct AccountScope
{
    private readonly string? _expectedKey;

    private AccountScope(string? expectedKey) => _expectedKey = expectedKey;

    /// <summary>A scope that accepts rows for any account.</summary>
    public static AccountScope Any { get; }

    /// <summary>Creates the scope for one import, or <see cref="Any"/> when no key is supplied.</summary>
    public static AccountScope For(string? expectedAccountKey) => new(expectedAccountKey);

    /// <summary>Whether a row whose account masks to <paramref name="accountKey"/> belongs to this scope.</summary>
    public bool Accepts(string accountKey) =>
        _expectedKey is null || string.Equals(accountKey, _expectedKey, StringComparison.Ordinal);
}
