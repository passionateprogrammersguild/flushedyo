namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Result of adapting one Fidelity CSV artifact: the mapped canonical transactions plus the
/// ambiguous rows that were refused. Ambiguous rows are never silently dropped.
/// </summary>
public sealed record CsvAdapterResult
{
    /// <summary>Gets the mapped rows in file order.</summary>
    public required IReadOnlyList<Transaction> Transactions { get; init; }

    /// <summary>Gets the ambiguous rows, in file order, each with a value-free reason.</summary>
    public required IReadOnlyList<AmbiguousRow> Ambiguous { get; init; }

    /// <summary>The empty result.</summary>
    public static CsvAdapterResult Empty { get; } = new()
    {
        Transactions = [],
        Ambiguous = [],
    };
}
