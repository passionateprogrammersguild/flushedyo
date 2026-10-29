namespace FlushedYo.Core.Models;

/// <summary>Whether a cash-only balance is usable.</summary>
public enum CashStateStatus
{
    /// <summary>A cash balance and signed movement were calculated from a known baseline and new cash movements.</summary>
    Known,

    /// <summary>No trustworthy balance can be calculated because required input is missing.</summary>
    Unknown,

    /// <summary>Stored activity requires manual reconciliation before a trustworthy balance can be calculated.</summary>
    ReconciliationNeeded,
}

/// <summary>Value-free reason for an unknown or reconciliation-needed cash state.</summary>
public enum CashStateReason
{
    /// <summary>No special reason; the cash state is known.</summary>
    None,

    /// <summary>No opening cash baseline or previously known cash balance is available.</summary>
    MissingBaseline,

    /// <summary>A downloaded record was rejected as invalid, or a stored transaction's cash treatment is ambiguous.</summary>
    AmbiguousTransaction,

    /// <summary>A correction requires explicit reconciliation.</summary>
    Correction,

    /// <summary>Unsupported activity requires explicit reconciliation.</summary>
    UnsupportedActivity,

    /// <summary>Cash arithmetic exceeded the supported <see cref="decimal"/> range.</summary>
    ArithmeticOverflow,
}

/// <summary>
/// A cash-only calculation checkpoint. This is never a total investment value and excludes holdings,
/// market valuation, and other non-cash activity.
/// </summary>
/// <param name="Status">Whether <see cref="CashBalance"/> and <see cref="Difference"/> are trustworthy.</param>
/// <param name="Reason">Why the state is unknown or needs reconciliation; <see cref="CashStateReason.None"/> when known.</param>
/// <param name="OpeningCashBalance">
/// Opening cash baseline used for a known calculation; otherwise <see langword="null"/>.
/// </param>
/// <param name="CashBalance">
/// New cash balance for a known calculation; otherwise <see langword="null"/>. This is not investment value.
/// </param>
/// <param name="Difference">
/// Signed cash movement (<see cref="CashBalance"/> minus <see cref="OpeningCashBalance"/>) for a known
/// calculation; otherwise <see langword="null"/>.
/// </param>
public sealed record CashState(
    CashStateStatus Status,
    CashStateReason Reason,
    decimal? OpeningCashBalance,
    decimal? CashBalance,
    decimal? Difference);
