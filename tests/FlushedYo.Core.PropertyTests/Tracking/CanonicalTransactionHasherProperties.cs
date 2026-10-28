using System.Globalization;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FlushedYo.Core.Models;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.PropertyTests.Tracking;

/// <summary>
/// Property family for canonical transaction hashing (spec: canonical_transaction_hashing).
/// Covers: cosmetic invariance, sensitivity to normalized-identity changes, canonical
/// output format, delimiter safety, and validity classification.
/// The hasher is a permanent contract — these properties document its current rules and must
/// never justify a rule change (plan: property-based testing introduction).
/// Example-level spec tests (known-answer vectors, validation error text, culture) remain in
/// <c>CanonicalTransactionHasherTests</c>.
/// </summary>
public sealed class CanonicalTransactionHasherProperties
{
    private static readonly Regex HashShape = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private static readonly Regex AmountShape = new("^-?[0-9]+\\.[0-9]{2,}$", RegexOptions.Compiled);
    private static readonly Regex DateShape = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.Compiled);

    /// <summary>The fixed, value-free validation reasons the hasher may report.</summary>
    private static readonly string[] ValidityReasons =
    [
        "institution is empty",
        "institution contains the '|' delimiter",
        "externalId is empty",
        "payee is empty",
    ];

    /// <summary>Values used to corrupt free-text fields, paired with their validation trigger kind.</summary>
    private static readonly (string? Value, string Reason)[] Corruptions =
    [
        (null, ""), // keeps the original valid value
        ("", "blank"),
        ("  ", "blank"),
        ("\t", "blank"),
        ("SEKRIT-PAYEE-4013", "valid-looking"),
        ("sektreT|ties almost|like real ids", "delimiter-wearing"),
        ("|stubs-9", "delimiter-wearing"),
        ("aa|bb", "delimiter-wearing"),
    ];

    // Property A — cosmetic invariance: a transaction and any of the cosmetic mutations the
    // canonicalization must ignore always canonicalize and hash identically.
    [Property(MaxTest = 100)]
    public Property CosmeticVariants_CanonicalizeAndHashTheSame() =>
        Prop.ForAll(TrackingGenerators.CosmeticPair().ToArbitrary(), pair =>
            CanonicalTransactionHasher.Canonicalize(pair.Canonical) == CanonicalTransactionHasher.Canonicalize(pair.Cosmetic)
            && CanonicalTransactionHasher.ComputeHash(pair.Canonical) == CanonicalTransactionHasher.ComputeHash(pair.Cosmetic));

    // Property B — sensitivity: changing exactly one hashed field's normalized value always
    // changes the hash. (Generator alphabet carries no '|' characters.)
    [Property(MaxTest = 100)]
    public Property DistinctNormalizedIdentities_HashDifferently() =>
        Prop.ForAll(TrackingGenerators.SensitizedPair().ToArbitrary(), pair =>
            CanonicalTransactionHasher.ComputeHash(pair.Canonical) != CanonicalTransactionHasher.ComputeHash(pair.Mutated));

    // Property C — output format: the canonical string is always exactly
    // institution|externalId|amount|date|payee with the documented per-field normalization,
    // and the hash is always 64 lowercase hex characters.
    [Property(MaxTest = 100)]
    public Property CanonicalString_MatchesTheDocumentedShape() =>
        Prop.ForAll(TrackingGenerators.Canonical().ToArbitrary(), transaction =>
        {
            var canonical = CanonicalTransactionHasher.Canonicalize(transaction);
            var parts = canonical.Split('|');
            var hasFiveParts = parts.Length == 5;

            var institutionField = string.Equals(parts[0], transaction.Institution.Trim(), StringComparison.OrdinalIgnoreCase);
            var externalIdField = parts[1] == transaction.ExternalId.Trim();
            var amountField = AmountShape.IsMatch(parts[2]);
            var dateField = DateShape.IsMatch(parts[3])
                && DateOnly.TryParseExact(parts[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                && parsedDate == transaction.Date;
            var payeeField = string.Equals(parts[4], parts[4].ToUpperInvariant(), StringComparison.Ordinal)
                && !parts[4].Contains("  ", StringComparison.Ordinal);

            return HashShape.IsMatch(CanonicalTransactionHasher.ComputeHash(transaction))
                && hasFiveParts
                && institutionField
                && externalIdField
                && amountField
                && dateField
                && payeeField;
        });

    // Property D — delimiter safety (documents current behavior): splicing '|' into externalId
    // and payee still never makes two distinct normalized identities share a hash. If this
    // property ever fails, the resolution is a documented known-behavior note — never a hasher
    // change (hash rules are migration territory).
    [Property(MaxTest = 100)]
    public Property DelimiterSplicing_NeverCollidesDistinctIdentities() =>
        Prop.ForAll(TrackingGenerators.DelimitedPair().ToArbitrary(), pair =>
            CanonicalTransactionHasher.ComputeHash(pair.First) != CanonicalTransactionHasher.ComputeHash(pair.Second));

    // Property E — validity/classification: TryValidate holds exactly the documented law
    // (all three free-text fields non-blank and institution '|' -free), failures report one of
    // the fixed value-free reasons, and the reason never leaks a field value.
    [Property(MaxTest = 100)]
    public Property TryValidate_HoldsTheDocumentedLaw() =>
        Prop.ForAll(MaybeInvalidTransaction().ToArbitrary(), transaction =>
        {
            var law = !string.IsNullOrWhiteSpace(transaction.Institution)
                && !transaction.Institution.Contains('|', StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(transaction.ExternalId)
                && !string.IsNullOrWhiteSpace(transaction.Payee);

            var isValid = CanonicalTransactionHasher.TryValidate(transaction, out var error);

            if (isValid != law)
            {
                return false;
            }

            if (isValid)
            {
                return error is null;
            }

            return error is not null
                && ValidityReasons.Contains(error)
                && !LeaksValue(error, transaction.Institution)
                && !LeaksValue(error, transaction.ExternalId)
                && !LeaksValue(error, transaction.Payee);
        });

    /// <summary>
    /// A valid generated transaction with 0..3 of its free-text fields replaced by
    /// generated blank, delimiter-wearing, or secret-looking values (spec: value-free
    /// validation reasons — the generator deliberately uses values that would leak).
    /// </summary>
    private static Gen<Transaction> MaybeInvalidTransaction() =>
        from transaction in TrackingGenerators.Canonical()
        from institution in MaybeCorrupt(transaction.Institution)
        from externalId in MaybeCorrupt(transaction.ExternalId)
        from payee in MaybeCorrupt(transaction.Payee)
        select transaction with
        {
            Institution = institution,
            ExternalId = externalId,
            Payee = payee,
        };

    /// <summary>Keeps the original value half the time; otherwise substitutes a corrupt variant.</summary>
    private static Gen<string> MaybeCorrupt(string original) =>
        Gen.Elements(Corruptions).Select(choice => choice.Value ?? original);

    /// <summary>
    /// A reason leaks when it contains a non-blank field value (blank values are ignored:
    /// every string contains the empty string).
    /// </summary>
    private static bool LeaksValue(string error, string value) =>
        !string.IsNullOrWhiteSpace(value) && error.Contains(value, StringComparison.Ordinal);
}
