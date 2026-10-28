namespace FlushedYo.Core.Models;

/// <summary>
/// A position as reported by a financial institution. Deliberately minimal: security-lot
/// tracking is out of scope (plan §8.4).
/// </summary>
public sealed record Holding
{
    /// <summary>Institution key that produced the holding, for example <c>fidelity</c>.</summary>
    public required string Institution { get; init; }

    /// <summary>Ticker or security symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Number of shares or units held.</summary>
    public required decimal Quantity { get; init; }

    /// <summary>Total market value of the position.</summary>
    public required decimal MarketValue { get; init; }

    /// <summary>Date the valuation applies to.</summary>
    public required DateOnly AsOf { get; init; }
}
