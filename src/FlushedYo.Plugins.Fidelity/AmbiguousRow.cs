namespace FlushedYo.Plugins.Fidelity;

/// <summary>Reasons a Fidelity CSV row is refused without guessing.</summary>
public enum AmbiguousReason
{
    /// <summary>The row has the wrong number of comma-separated fields.</summary>
    MalformedRow,

    /// <summary>The row has no readable date.</summary>
    MissingDate,

    /// <summary>The row has no readable amount.</summary>
    MissingAmount,

    /// <summary>The amount carries no explicit sign or parentheses and cannot be interpreted.</summary>
    AmbiguousSign,

    /// <summary>The row has no account number.</summary>
    MissingAccount,

    /// <summary>The row has no payee name or description.</summary>
    MissingPayee,

    /// <summary>The row's account number masks to a different account than the one being imported.</summary>
    AccountMismatch,
}

/// <summary>
/// A CSV row that the adapter refused to map to a canonical transaction without guessing.
/// The row is never discarded; the reason is all the caller gets.
/// </summary>
public sealed record AmbiguousRow
{
    /// <summary>Zero-based line index of the row inside the CSV file, including the header line.</summary>
    public required int LineNumber { get; init; }

    /// <summary>The value-free reason the row is ambiguous.</summary>
    public required AmbiguousReason Reason { get; init; }
}
