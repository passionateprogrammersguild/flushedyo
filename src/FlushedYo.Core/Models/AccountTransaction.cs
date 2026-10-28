namespace FlushedYo.Core.Models;

/// <summary>How an account transaction is allowed to affect a cash-only balance calculation.</summary>
public enum CashImpact
{
    /// <summary>The signed transaction amount is a cash movement and can be applied to a cash baseline.</summary>
    CashAffecting,

    /// <summary>The transaction is known not to move cash and is ignored by cash-state calculation.</summary>
    NotCashAffecting,

    /// <summary>The transaction's cash treatment is not known; reconciliation is required.</summary>
    Unknown,

    /// <summary>The transaction corrects earlier activity and requires explicit reconciliation.</summary>
    Correction,

    /// <summary>The activity is not supported by the cash-only model and requires reconciliation.</summary>
    Unsupported,
}

/// <summary>A transaction together with its stable account identity and explicit cash classification.</summary>
public sealed record AccountTransaction
{
    /// <summary>Account to which the transaction belongs.</summary>
    public required Account Account { get; init; }

    /// <summary>Institution-reported transaction fields.</summary>
    public required Transaction Transaction { get; init; }

    /// <summary>Whether and how this activity may affect cash. Never inferred from the free-form transaction type.</summary>
    public required CashImpact CashImpact { get; init; }
}
