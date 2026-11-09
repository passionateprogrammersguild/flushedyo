using FlushedYo.Core.Models;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Outcome of one account's download, adaptation and import. Counts and value-free reasons only,
/// never a payee, amount, account number, hash, or secret.
/// </summary>
public sealed record FidelityAccountDownload
{
    /// <summary>Gets the account this result belongs to.</summary>
    public required Account Account { get; init; }

    /// <summary>Gets the number of mapped rows in the artifact.</summary>
    public required int Mapped { get; init; }

    /// <summary>Gets the number of rows the adapter refused without guessing.</summary>
    public required int Ambiguous { get; init; }

    /// <summary>Gets the canonical transactions this account's artifact mapped to, in file order.</summary>
    public IReadOnlyList<Transaction> Transactions { get; init; } = [];

    /// <summary>Gets the persisted import result, or <see langword="null"/> when the account failed.</summary>
    public FlushedYo.Core.Store.AccountImportResult? Import { get; init; }

    /// <summary>
    /// Gets the value-free reason the account failed, or <see langword="null"/> when it succeeded.
    /// A failed account never advances its watermark because it never imports.
    /// </summary>
    public string? Failure { get; init; }

    /// <summary>Gets a value indicating whether the account completed its import.</summary>
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Outcome of a whole multi-account Fidelity run: one entry per downloaded account plus the mapped
/// transactions flattened for legacy <c>IProviderPlugin</c> callers.
/// </summary>
public sealed record FidelityDownloadResult
{
    /// <summary>Gets one result per account, in source enumeration order.</summary>
    public required IReadOnlyList<FidelityAccountDownload> Accounts { get; init; }

    /// <summary>Gets every mapped transaction across accounts, in source order.</summary>
    public required IReadOnlyList<Transaction> Transactions { get; init; }

    /// <summary>Gets the number of accounts whose import completed.</summary>
    public int Succeeded => Accounts.Count(static account => account.Succeeded);

    /// <summary>Gets the number of accounts that failed.</summary>
    public int Failed => Accounts.Count(static account => !account.Succeeded);
}
