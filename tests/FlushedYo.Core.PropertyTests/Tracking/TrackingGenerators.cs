using System.Globalization;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FlushedYo.Core.Models;

namespace FlushedYo.Core.PropertyTests;

/// <summary>
/// Generators for transactions in their canonical form plus cosmetic variants of it.
/// A <em>cosmetic</em> mutation is one the canonicalization must ignore (case of institution or
/// payee, whitespace, decimal scale, the sign of zero, the value of <see cref="Transaction.Type"/>),
/// so a pair built by <see cref="CosmeticPair"/> must always hash equal. A <em>significant</em>
/// mutation (produced by <see cref="SensitizedPair"/>) changes the normalized value of exactly one
/// hashed field, so such a pair must always hash differently.
/// </summary>
internal static class TrackingGenerators
{
    private static readonly string[] Institutions = ["fidelity", "fidelity-bank", "schwab", "usaa", "ally-bank"];
    private static readonly string[] PayeeWords = ["Corner", "Lake", "Hive", "Union", "Market", "Nord", "Peak", "Store"];
    private static readonly string[] ExternalIdPrefixes = ["tx", "ab", "op", "tr"];
    private static readonly string[] ExternalIdSuffixes = ["", "-x", "-ab", "-9"];
    private static readonly string[] WhitespaceRuns = [" ", "  ", "\t", " \t ", "\n", " \r\n \t"];
    private static readonly string[] Types = ["debit", "credit", "fee", "", "transfer"];

    /// <summary>A canonical transaction with no delimiter characters in any free-text field.</summary>
    public static Gen<Transaction> Canonical() =>
        from institution in Gen.Elements(Institutions)
        from externalId in ExternalId()
        from amount in AmountTwoDecimals()
        from date in Date()
        from payee in Payee()
        from type in Gen.Elements(Types)
        select new Transaction
        {
            Institution = institution,
            ExternalId = externalId,
            Amount = amount,
            Date = date,
            Payee = payee,
            Type = type,
        };

    /// <summary>An external id without delimiters, deterministically built from small parts.</summary>
    public static Gen<string> ExternalId() =>
        from prefix in Gen.Elements(ExternalIdPrefixes)
        from number in Gen.Choose(0, 9_999)
        from suffix in Gen.Elements(ExternalIdSuffixes)
        select prefix + number.ToString(NumberFormatInfo.InvariantInfo) + suffix;

    /// <summary>A payee built from one to three words, so payee variants stay readable.</summary>
    public static Gen<string> Payee() =>
        from count in Gen.Choose(1, 3)
        from words in Gen.ListOf(Gen.Elements(PayeeWords), count)
        select string.Join(" ", words);

    /// <summary>An amount with exactly two decimals (the most common real-world scale).</summary>
    public static Gen<decimal> AmountTwoDecimals() =>
        Gen.Choose(-99_999, 99_999).Select(cents => cents / 100m);

    /// <summary>A date within roughly five hundred years of the present, avoiding the calendar edges.</summary>
    public static Gen<DateOnly> Date() =>
        Gen.Choose(-18_2500, 18_2500).Select(days => new DateOnly(2026, 1, 1).AddDays(days));

    /// <summary>
    /// A transaction that shares <paramref name="baseTransaction"/>'s canonical hash but may differ
    /// in its original text and <see cref="Transaction.Type"/> (a cosmetic mutation). Idempotency
    /// scenarios use it to re-track an identity with different surface text.
    /// </summary>
    public static Gen<Transaction> VariantOf(Transaction baseTransaction) =>
        from institution in CosmeticInstitution(baseTransaction.Institution)
        from externalId in CosmeticExternalId(baseTransaction.ExternalId)
        from amount in CosmeticAmount(baseTransaction.Amount)
        from payee in CosmeticPayee(baseTransaction.Payee)
        from type in Gen.Elements(Types)
        select baseTransaction with
        {
            Institution = institution,
            ExternalId = externalId,
            Amount = amount,
            Payee = payee,
            Type = type,
        };

    /// <summary>A pair of transactions that must hash identically: one canonical, one cosmetically mutated.</summary>
    public static Gen<(Transaction Canonical, Transaction Cosmetic)> CosmeticPair() =>
        from canonical in Canonical()
        from institution in CosmeticInstitution(canonical.Institution)
        from externalId in CosmeticExternalId(canonical.ExternalId)
        from amount in CosmeticAmount(canonical.Amount)
        from payee in CosmeticPayee(canonical.Payee)
        from type in Gen.Elements(Types)
        let cosmetic = canonical with
        {
            Institution = institution,
            ExternalId = externalId,
            Amount = amount,
            Payee = payee,
            Type = type,
        }
        select (canonical, cosmetic);

    /// <summary>A pair of transactions whose normalized identity differs in exactly one hashed field.</summary>
    public static Gen<(Transaction Canonical, Transaction Mutated)> SensitizedPair() =>
        from canonical in Canonical()
        from mutated in MutateHashedField(canonical)
        select (canonical, mutated);

    /// <summary>
    /// A pair of two transactions with distinct normalized identities where the free-text fields
    /// (<c>externalId</c>, <c>payee</c>) carry the <c>'|'</c> delimiter. Canonicalization only
    /// rejects the delimiter in <c>institution</c>, so this documents that no other field can make
    /// two distinct identities share a hash (see the hasher doc comments).
    /// </summary>
    public static Gen<(Transaction First, Transaction Second)> DelimitedPair() =>
        from first in Canonical()
        from secondExternalId in DelimitedExternalId(first.ExternalId)
        from secondPayee in DelimitedPayee(first.ExternalId, first.Amount, first.Date)
        let second = first with
        {
            ExternalId = secondExternalId,
            Payee = secondPayee,
        }
        select (first, second);

    /// <summary>Changes the normalized value of exactly one hashed field of <paramref name="canonical"/>.</summary>
    public static Gen<Transaction> MutateHashedField(Transaction canonical) =>
        from mutated in Gen.OneOf(
            DifferentInstitution(canonical.Institution).Select(i => canonical with { Institution = i }),
            DifferentExternalId(canonical.ExternalId).Select(e => canonical with { ExternalId = e }),
            DifferentAmount(canonical.Amount).Select(a => canonical with { Amount = a }),
            DifferentDate(canonical.Date).Select(d => canonical with { Date = d }),
            DifferentPayee(canonical.Payee).Select(p => canonical with { Payee = p }))
        select mutated;

    /// <summary>An external id that differs from the given one but contains a delimiter.</summary>
    private static Gen<string> DelimitedExternalId(string original) =>
        ExternalId()
            .Where(candidate => candidate != original)
            .Select(candidate =>
            {
                var cut = Math.Max(1, candidate.Length / 2);
                return candidate[..cut] + "|" + candidate[cut..];
            });

    /// <summary>
    /// A payee that differs from the original's uppercase form and splices in amount- and
    /// date-shaped tokens with delimiters — the tokens most likely to mis-align the canonical
    /// string segments if a cross-field collision were possible at all.
    /// </summary>
    private static Gen<string> DelimitedPayee(string originalExternalId, decimal amount, DateOnly date) =>
        from extra in DelimitedExternalId(originalExternalId)
        from extraPayee in Payee()
        select string.Join("|", extraPayee, amount.ToString(CultureInfo.InvariantCulture), date.ToString(CultureInfo.InvariantCulture), extra);

    /// <summary>Surrounding whitespace and case variants that canonicalization must trim and fold.</summary>
    private static Gen<string> CosmeticInstitution(string institution) =>
        from wrap in WhitespaceWrap()
        from fold in CaseFold()
        select wrap(fold(institution));

    /// <summary>Only surrounding whitespace is cosmetic for external ids; case is significant.</summary>
    private static Gen<string> CosmeticExternalId(string externalId) =>
        WhitespaceWrap().Select(wrap => wrap(externalId));

    /// <summary>Scale-only variants that keep the numeric value; the sign of zero is never significant.</summary>
    private static Gen<decimal> CosmeticAmount(decimal amount) =>
        amount == 0m
            ? Gen.Elements(0m, -0.0m, 0.00m, -0.000m)
            : from zeros in Gen.Choose(2, 6)
              let digits = amount.ToString(CultureInfo.InvariantCulture)
              select decimal.Parse(
                  digits.Contains('.', StringComparison.Ordinal)
                      ? digits + new string('0', zeros)
                      : digits + "." + new string('0', zeros),
                  NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                  CultureInfo.InvariantCulture);

    /// <summary>Whitespace-rewriting and case-folding variants that canonicalization must collapse.</summary>
    private static Gen<string> CosmeticPayee(string payee) =>
        from separator in Gen.Elements(WhitespaceRuns)
        from fold in CaseFold()
        from wrap in WhitespaceWrap()
        let words = payee.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        select words.Length == 0 ? payee : wrap(fold(string.Join(separator, words)));

    /// <summary>A different lowercase institution name.</summary>
    private static Gen<string> DifferentInstitution(string institution) =>
        Gen.Elements(Institutions).Where(candidate => candidate != institution);

    /// <summary>
    /// A different external id: half the cases flip the case of the original (identity is
    /// case-sensitive), the rest pick an unrelated generated id. The trimmed normalized value
    /// always differs from the original's.
    /// </summary>
    private static Gen<string> DifferentExternalId(string externalId) =>
        Gen.OneOf(
            FlipCase(externalId),
            ExternalId().Where(candidate => candidate != externalId));

    private static Gen<string> FlipCase(string value) =>
        Gen.Constant(new string(value
            .Select(character => char.IsUpper(character) ? char.ToLowerInvariant(character) : char.ToUpperInvariant(character))
            .ToArray()));

    /// <summary>
    /// An amount that differs from the original by at least a sub-cent increment, so its
    /// canonical digits always differ. Whole-cent changes and sub-cent changes both count:
    /// the canonical format keeps decimals beyond the second significant.
    /// </summary>
    private static Gen<decimal> DifferentAmount(decimal amount) =>
        from sign in Gen.Elements(1m, -1m)
        from cents in Gen.Choose(1, 200)
        from mills in Gen.Choose(0, 9)
        select amount + (sign * (cents / 100m)) + (sign * (mills / 1000m));

    /// <summary>A date a few calendar days away from the original (never the same day).</summary>
    private static Gen<DateOnly> DifferentDate(DateOnly date) =>
        Gen.Choose(-3_650, 3_650).Where(days => days != 0).Select(days => date.AddDays(days));

    /// <summary>
    /// A payee whose uppercase, whitespace-collapsed form differs from the original's. The filter
    /// mirrors the hasher's own payee normalization — the generator needs the guarantees the
    /// property relies on, but the contract itself stays pinned to the hasher.
    /// </summary>
    private static Gen<string> DifferentPayee(string payee) =>
        Payee().Where(candidate => NormalizedPayee(candidate) != NormalizedPayee(payee));

    /// <summary>Generator-side replica of the canonical payee normalization, used for filtering only.</summary>
    private static string NormalizedPayee(string payee) =>
        WhitespaceRun.Replace(payee.Trim(), " ").ToUpperInvariant();

    /// <summary>Whitespace wrappers that canonicalization must trim from both ends.</summary>
    private static Gen<Func<string, string>> WhitespaceWrap() =>
        from leading in Gen.Elements(WhitespaceRuns)
        from trailing in Gen.Elements(WhitespaceRuns)
        select (Func<string, string>)(value => leading + value + trailing);

    /// <summary>Identity or case folds that canonicalization must ignore.</summary>
    private static Gen<Func<string, string>> CaseFold() =>
        Gen.Elements<Func<string, string>>(
            value => value,
            value => value.ToUpperInvariant(),
            value => value.ToLowerInvariant());

    /// <summary>Generator-side replica of the canonical whitespace-run collapse, used for filtering only.</summary>
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
}
