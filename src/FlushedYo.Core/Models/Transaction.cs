namespace FlushedYo.Core.Models;

/// <summary>
/// A transaction as reported by a financial institution, using the canonical field set
/// from plan §5. Normalization and hashing happen later, in the transaction tracker.
/// </summary>
public sealed record Transaction
{
    /// <summary>Institution key that produced the transaction, for example <c>fidelity</c>.</summary>
    public required string Institution { get; init; }

    /// <summary>Institution-assigned transaction identifier.</summary>
    public required string ExternalId { get; init; }

    /// <summary>Signed amount. Negative means money out.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Posting or transaction date as reported by the institution.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Counterparty name as reported by the institution.</summary>
    public required string Payee { get; init; }

    /// <summary>Free-form transaction type as reported, for example <c>debit</c> or <c>credit</c>.</summary>
    public required string Type { get; init; }
}
