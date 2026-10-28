using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FlushedYo.Core.Models;

namespace FlushedYo.Core.Tracking;

/// <summary>
/// Validates a <see cref="Transaction"/>, builds its canonical string, and computes the SHA-256
/// dedup key stored in the ledger (plan §5).
/// </summary>
/// <remarks>
/// <para>
/// The canonical form is <c>{institution}|{externalId}|{amount}|{date}|{payee}</c> where
/// <c>institution</c> is trimmed and lowercased, <c>externalId</c> is trimmed (case preserved),
/// <c>amount</c> has at least two decimals with further significant digits kept, <c>date</c> is
/// <c>yyyy-MM-dd</c>, and <c>payee</c> is trimmed, whitespace-collapsed, and uppercased.
/// <see cref="Transaction.Type"/> is not part of the hash. All formatting is culture-invariant.
/// </para>
/// <para>
/// <b>The hash is a permanent contract.</b> Hashes already stored in a ledger must keep matching
/// in future releases, so changing any rule here requires a ledger migration.
/// </para>
/// </remarks>
public static partial class CanonicalTransactionHasher
{
    private const char Delimiter = '|';
    private const string AmountFormat = "0.00############################";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Checks whether <paramref name="transaction"/> can be hashed.</summary>
    /// <param name="transaction">The transaction to check.</param>
    /// <param name="error">
    /// A short reason that never includes field values, or <see langword="null"/> when the transaction is valid.
    /// </param>
    /// <returns><see langword="true"/> when the transaction is valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    public static bool TryValidate(Transaction transaction, out string? error)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (string.IsNullOrWhiteSpace(transaction.Institution))
        {
            error = "institution is empty";
        }
        else if (transaction.Institution.Contains(Delimiter, StringComparison.Ordinal))
        {
            error = "institution contains the '|' delimiter";
        }
        else if (string.IsNullOrWhiteSpace(transaction.ExternalId))
        {
            error = "externalId is empty";
        }
        else if (string.IsNullOrWhiteSpace(transaction.Payee))
        {
            error = "payee is empty";
        }
        else
        {
            error = null;
        }

        return error is null;
    }

    /// <summary>Builds the canonical string that is hashed.</summary>
    /// <param name="transaction">The transaction to canonicalize.</param>
    /// <returns>The normalized <c>institution|externalId|amount|date|payee</c> string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The transaction is invalid (see <see cref="TryValidate"/>).</exception>
    public static string Canonicalize(Transaction transaction)
    {
        if (!TryValidate(transaction, out var error))
        {
            throw new ArgumentException(error, nameof(transaction));
        }

        var institution = transaction.Institution.Trim().ToLowerInvariant();
        var externalId = transaction.ExternalId.Trim();
        var amount = transaction.Amount.ToString(AmountFormat, CultureInfo.InvariantCulture);
        var date = transaction.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
        var payee = WhitespaceRun().Replace(transaction.Payee.Trim(), " ").ToUpperInvariant();

        return string.Join(Delimiter, institution, externalId, amount, date, payee);
    }

    /// <summary>Computes the canonical dedup hash for <paramref name="transaction"/>.</summary>
    /// <param name="transaction">The transaction to hash.</param>
    /// <returns>64 lowercase hexadecimal characters (SHA-256 of the UTF-8 canonical string).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The transaction is invalid (see <see cref="TryValidate"/>).</exception>
    public static string ComputeHash(Transaction transaction) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(transaction))));

    // \s matches Unicode whitespace in .NET, consistent with char.IsWhiteSpace.
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
