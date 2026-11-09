namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Institution identity used when mapping Fidelity rows: the institution key and a per-run
/// masker that turns a raw account number into a stable, log-safe account key.
/// </summary>
public sealed record InstitutionIdentity
{
    /// <summary>The institution key, for example <c>fidelity</c>.</summary>
    public required string Institution { get; init; }

    /// <summary>
    /// Derives the stable, account-scoped external identifier for one CSV row. The identifier must
    /// be deterministic across runs and must never be manufactured from thin air.
    /// </summary>
    /// <param name="accountKey">The masked, stable account key.</param>
    /// <param name="date">The row date.</param>
    /// <param name="amount">The signed amount.</param>
    /// <param name="memo">The row memo or description.</param>
    /// <returns>The stable identifier.</returns>
    public required Func<string, DateOnly, decimal, string, string> StableId { get; init; }

    /// <summary>
    /// Masks a raw account number into a stable, log-safe account key. Implementations must be
    /// deterministic across runs and must never echo the raw number.
    /// </summary>
    public required Func<string, string> MaskAccount { get; init; }

    /// <summary>The default Fidelity identity: institution <c>fidelity</c> with a SHA-256 stable identifier.</summary>
    public static InstitutionIdentity Fidelity { get; } = new()
    {
        Institution = "fidelity",
        MaskAccount = static accountNumber =>
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(accountNumber))),
        StableId = (accountKey, date, amount, memo) =>
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"{accountKey}|{date:yyyy-MM-dd}|{amount}|{memo.Trim()}")))),
    };
}
