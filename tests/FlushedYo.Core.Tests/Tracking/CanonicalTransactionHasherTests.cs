using FlushedYo.Core.Models;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>
/// Known-answer vectors and validation documentation (spec: canonical_transaction_hashing).
/// Expected hashes were computed independently with <c>shasum -a 256</c>, never from the code under test.
/// Normalization/hash invariants are covered as properties in
/// <c>FlushedYo.Core.PropertyTests.Tracking.CanonicalTransactionHasherProperties</c>.
/// </summary>
public sealed class CanonicalTransactionHasherTests
{
    private static Transaction Tx(
        string institution = "fidelity",
        string externalId = "abc-123",
        decimal amount = -12.30m,
        DateOnly? date = null,
        string payee = "Corner Store",
        string type = "debit") =>
        new()
        {
            Institution = institution,
            ExternalId = externalId,
            Amount = amount,
            Date = date ?? new DateOnly(2026, 1, 2),
            Payee = payee,
            Type = type,
        };

    private static string Hash(Transaction transaction) => CanonicalTransactionHasher.ComputeHash(transaction);

    [Fact]
    public void KnownAnswer_Defaults()
    {
        var transaction = Tx();

        Assert.Equal("fidelity|abc-123|-12.30|2026-01-02|CORNER STORE", CanonicalTransactionHasher.Canonicalize(transaction));
        Assert.Equal("d658cade68a8a86b84124a235261b596ae6e24dda9be67ed7fab62c4d430cb24", Hash(transaction));
    }

    [Fact]
    public void KnownAnswer_WholeAmount()
    {
        var transaction = Tx(externalId: "1", amount: 5m, date: new DateOnly(2026, 1, 1), payee: "Test");

        Assert.Equal("fidelity|1|5.00|2026-01-01|TEST", CanonicalTransactionHasher.Canonicalize(transaction));
        Assert.Equal("49d4c94b5a4138d9c13124137c145e5dd0806343d5123fb39a0526a6a3838a63", Hash(transaction));
    }

    [Fact]
    public void KnownAnswer_NegativeZeroAmount()
    {
        var transaction = Tx(externalId: "1", amount: -0.0m, date: new DateOnly(2026, 1, 1), payee: "Test");

        Assert.Equal("fidelity|1|0.00|2026-01-01|TEST", CanonicalTransactionHasher.Canonicalize(transaction));
        Assert.Equal("e72a73a4d103a62c0718ca6a69515fc4dbf83f81d775b2c7a7e2cb0eff07a86a", Hash(transaction));
    }

    [Fact]
    public void TryValidate_ValidTransaction_ReturnsTrueWithNoError()
    {
        Assert.True(CanonicalTransactionHasher.TryValidate(Tx(), out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_EmptyType_IsValid()
    {
        Assert.True(CanonicalTransactionHasher.TryValidate(Tx(type: ""), out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("", "institution is empty")]
    [InlineData("   ", "institution is empty")]
    [InlineData("a|b", "institution contains the '|' delimiter")]
    public void TryValidate_InvalidInstitution_ReportsReason(string institution, string expectedError)
    {
        Assert.False(CanonicalTransactionHasher.TryValidate(Tx(institution: institution), out var error));
        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_EmptyExternalId_ReportsReason(string externalId)
    {
        Assert.False(CanonicalTransactionHasher.TryValidate(Tx(externalId: externalId), out var error));
        Assert.Equal("externalId is empty", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_EmptyPayee_ReportsReason(string payee)
    {
        Assert.False(CanonicalTransactionHasher.TryValidate(Tx(payee: payee), out var error));
        Assert.Equal("payee is empty", error);
    }

    [Fact]
    public void TryValidate_MultipleProblems_ReportsFirstInTableOrder()
    {
        var transaction = Tx(institution: "", externalId: "", payee: "");

        Assert.False(CanonicalTransactionHasher.TryValidate(transaction, out var error));
        Assert.Equal("institution is empty", error);
    }

    [Fact]
    public void TryValidate_ErrorNeverContainsFieldValues()
    {
        Assert.False(CanonicalTransactionHasher.TryValidate(Tx(institution: "", payee: "SECRET-PAYEE", externalId: "SECRET-ID"), out var error));
        Assert.DoesNotContain("SECRET-PAYEE", error, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-ID", error, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidTransaction_CanonicalizeAndComputeHashThrowWithTheValidationError()
    {
        var transaction = Tx(externalId: "");
        CanonicalTransactionHasher.TryValidate(transaction, out var expected);

        var canonicalize = Assert.Throws<ArgumentException>(() => CanonicalTransactionHasher.Canonicalize(transaction));
        var computeHash = Assert.Throws<ArgumentException>(() => CanonicalTransactionHasher.ComputeHash(transaction));

        Assert.StartsWith(expected!, canonicalize.Message, StringComparison.Ordinal);
        Assert.StartsWith(expected!, computeHash.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullTransaction_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CanonicalTransactionHasher.TryValidate(null!, out _));
        Assert.Throws<ArgumentNullException>(() => CanonicalTransactionHasher.Canonicalize(null!));
        Assert.Throws<ArgumentNullException>(() => CanonicalTransactionHasher.ComputeHash(null!));
    }
}
