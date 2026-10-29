using FlushedYo.Core.Models;
using FlushedYo.Core.Tests.Store;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>Pinned account-scoped hash vectors and identity validation.</summary>
public sealed class AccountTransactionHasherTests
{
    [Fact]
    public void KnownAnswer_DefaultAccountAndTransaction()
    {
        var transaction = Tx();

        Assert.Equal(
            "8:fidelity9:account-1d658cade68a8a86b84124a235261b596ae6e24dda9be67ed7fab62c4d430cb24",
            AccountTransactionHasher.Canonicalize(transaction));
        Assert.Equal(
            "c7c1d29f4a65d0ec19346fd94054eba866316998580c025205332c8f1aa4a3c9",
            AccountTransactionHasher.ComputeHash(transaction));
        Assert.Equal(
            "d658cade68a8a86b84124a235261b596ae6e24dda9be67ed7fab62c4d430cb24",
            CanonicalTransactionHasher.ComputeHash(transaction.Transaction));
    }

    [Fact]
    public void EquivalentLegacyTransactions_InDifferentAccounts_DoNotCollide()
    {
        var first = Tx(accountKey: "account-1");
        var second = Tx(accountKey: "account-2");

        Assert.Equal(
            CanonicalTransactionHasher.ComputeHash(first.Transaction),
            CanonicalTransactionHasher.ComputeHash(second.Transaction));
        Assert.NotEqual(AccountTransactionHasher.ComputeHash(first), AccountTransactionHasher.ComputeHash(second));
    }

    [Fact]
    public void SameAccountKey_InDifferentInstitutions_DoesNotCollide()
    {
        var first = Tx(institution: "fidelity");
        var second = Tx(institution: "other");

        Assert.NotEqual(AccountTransactionHasher.ComputeHash(first), AccountTransactionHasher.ComputeHash(second));
    }

    [Fact]
    public void InstitutionAndAccountKeyNormalization_MatchesDocumentedCaseRules()
    {
        var baseline = Tx(institution: "fidelity", accountKey: "account-1");
        var normalized = Tx(institution: "  FIDELITY  ", accountKey: "  account-1  ");
        var differentKeyCase = Tx(institution: "FIDELITY", accountKey: "ACCOUNT-1");

        Assert.Equal(AccountTransactionHasher.ComputeHash(baseline), AccountTransactionHasher.ComputeHash(normalized));
        Assert.NotEqual(AccountTransactionHasher.ComputeHash(baseline), AccountTransactionHasher.ComputeHash(differentKeyCase));
    }

    [Fact]
    public void CashClassificationAndLegacyType_DoNotChangeScopedIdentity()
    {
        var baseline = Tx();
        var changed = baseline with
        {
            CashImpact = CashImpact.Unknown,
            Transaction = baseline.Transaction with { Type = "corrected" },
        };

        Assert.Equal(AccountTransactionHasher.ComputeHash(baseline), AccountTransactionHasher.ComputeHash(changed));
    }

    [Theory]
    [InlineData("", "account-1", "account institution is empty")]
    [InlineData("fidelity", " ", "account key is empty")]
    public void TryValidateAccount_InvalidIdentity_ReturnsValueFreeReason(
        string institution,
        string accountKey,
        string expectedError)
    {
        var valid = AccountTransactionHasher.TryValidateAccount(
            new Account { Institution = institution, AccountKey = accountKey },
            out var error);

        Assert.False(valid);
        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void TryValidateAccount_AccountKeyLengthAfterTrimming_EnforcesBoundary(int length, bool expectedValid)
    {
        var key = $" {new string('a', length)} ";

        var valid = AccountTransactionHasher.TryValidateAccount(
            new Account { Institution = "fidelity", AccountKey = key },
            out var error);

        Assert.Equal(expectedValid, valid);
        Assert.Equal(expectedValid ? null : "account key is too long", error);
    }

    [Theory]
    [InlineData("account\nkey")]
    [InlineData("account\tkey")]
    [InlineData("account\u001fkey")]
    [InlineData("account\u007fkey")]
    [InlineData("account\u0085key")]
    public void TryValidateAccount_ControlCharacterInAccountKey_ReturnsValueFreeReason(string accountKey)
    {
        var valid = AccountTransactionHasher.TryValidateAccount(
            new Account { Institution = "fidelity", AccountKey = accountKey },
            out var error);

        Assert.False(valid);
        Assert.Equal("account key contains a control character", error);
        Assert.DoesNotContain(accountKey, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_MismatchedInstitutions_FailsClosed()
    {
        var transaction = Tx() with { Transaction = Tx().Transaction with { Institution = "other" } };

        Assert.False(AccountTransactionHasher.TryValidate(transaction, out var error));
        Assert.Equal("account and transaction institutions differ", error);
    }

    [Fact]
    public void TryValidate_UndefinedCashImpact_FailsClosed()
    {
        var transaction = Tx() with { CashImpact = (CashImpact)999 };

        Assert.False(AccountTransactionHasher.TryValidate(transaction, out var error));
        Assert.Equal("cash impact is not defined", error);
    }

    private static AccountTransaction Tx(string institution = "fidelity", string accountKey = "account-1") =>
        AccountLedgerTestData.AccountTx(
            account: new Account { Institution = institution, AccountKey = accountKey },
            transactionInstitution: institution);
}
