using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FlushedYo.Core.Models;

namespace FlushedYo.Core.Tracking;

/// <summary>
/// Validates account transactions and computes an account-scoped SHA-256 dedup key without changing
/// the permanent legacy <see cref="CanonicalTransactionHasher"/> contract.
/// </summary>
/// <remarks>
/// The scoped payload is the trimmed lowercase institution and trimmed case-sensitive account key,
/// each prefixed by its UTF-16 length, followed by the transaction's legacy canonical hash. Length
/// prefixes make the payload unambiguous without restricting opaque account keys. Equivalent legacy
/// transactions in different accounts therefore have different scoped hashes.
/// </remarks>
public static class AccountTransactionHasher
{
    /// <summary>Checks whether an account identity is valid.</summary>
    /// <param name="account">The account to check.</param>
    /// <param name="error">A short value-free reason, or <see langword="null"/> when valid.</param>
    /// <returns><see langword="true"/> when the account is valid.</returns>
    public static bool TryValidateAccount(Account account, out string? error)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(account.Institution))
        {
            error = "account institution is empty";
        }
        else if (string.IsNullOrWhiteSpace(account.AccountKey))
        {
            error = "account key is empty";
        }
        else
        {
            error = null;
        }

        return error is null;
    }

    /// <summary>Checks whether an account transaction can be assigned a scoped dedup key.</summary>
    /// <param name="accountTransaction">The account transaction to check.</param>
    /// <param name="error">A short value-free reason, or <see langword="null"/> when valid.</param>
    /// <returns><see langword="true"/> when the account transaction is valid.</returns>
    public static bool TryValidate(AccountTransaction accountTransaction, out string? error)
    {
        ArgumentNullException.ThrowIfNull(accountTransaction);

        if (accountTransaction.Account is null)
        {
            error = "account is null";
            return false;
        }

        if (!TryValidateAccount(accountTransaction.Account, out error))
        {
            return false;
        }

        if (accountTransaction.Transaction is null)
        {
            error = "transaction is null";
            return false;
        }

        if (!CanonicalTransactionHasher.TryValidate(accountTransaction.Transaction, out error))
        {
            return false;
        }

        if (!string.Equals(
                accountTransaction.Account.Institution.Trim(),
                accountTransaction.Transaction.Institution.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            error = "account and transaction institutions differ";
            return false;
        }

        if (!Enum.IsDefined(accountTransaction.CashImpact))
        {
            error = "cash impact is not defined";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Builds the unambiguous account-scoped payload that is hashed.</summary>
    /// <param name="accountTransaction">The account transaction to canonicalize.</param>
    /// <returns>The length-prefixed account identity followed by the legacy canonical hash.</returns>
    /// <exception cref="ArgumentException">The account transaction is invalid.</exception>
    public static string Canonicalize(AccountTransaction accountTransaction)
    {
        if (!TryValidate(accountTransaction, out var error))
        {
            throw new ArgumentException(error, nameof(accountTransaction));
        }

        var account = NormalizeAccount(accountTransaction.Account);
        var legacyHash = CanonicalTransactionHasher.ComputeHash(accountTransaction.Transaction);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{account.Institution.Length}:{account.Institution}{account.AccountKey.Length}:{account.AccountKey}{legacyHash}");
    }

    /// <summary>Computes the account-scoped dedup hash.</summary>
    /// <param name="accountTransaction">The account transaction to hash.</param>
    /// <returns>64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentException">The account transaction is invalid.</exception>
    public static string ComputeHash(AccountTransaction accountTransaction) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(accountTransaction))));

    internal static Account NormalizeAccount(Account account)
    {
        if (!TryValidateAccount(account, out var error))
        {
            throw new ArgumentException(error, nameof(account));
        }

        return new Account
        {
            Institution = account.Institution.Trim().ToLowerInvariant(),
            AccountKey = account.AccountKey.Trim(),
        };
    }
}
