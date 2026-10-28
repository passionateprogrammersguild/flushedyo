using FlushedYo.Core.Models;

namespace FlushedYo.Core.Store;

/// <summary>The persisted outcome of one account import.</summary>
public sealed record AccountImportResult
{
    /// <summary>Number of newly inserted account transactions.</summary>
    public required int Inserted { get; init; }

    /// <summary>Number of valid transactions already present in this account.</summary>
    public required int Skipped { get; init; }

    /// <summary>Number of downloaded records rejected before storage.</summary>
    public required int Rejected { get; init; }

    /// <summary>
    /// Only the rows inserted by this import, in insertion order. Cash calculation applies this list once;
    /// duplicate downloads are excluded.
    /// </summary>
    public required IReadOnlyList<AccountTransaction> NewlyInsertedTransactions { get; init; }

    /// <summary>
    /// Current inclusive safe-date watermark for the account after this import, or <see langword="null"/>
    /// when no safe boundary has been established. It must be overlapped, never used as an exclusive filter.
    /// </summary>
    public required DateOnly? LatestSafeDate { get; init; }

    /// <summary>Cash-only progress after this import. This is not total investment value.</summary>
    public required CashState CashState { get; init; }

    /// <summary>Total downloaded records represented by this result.</summary>
    public int Total => Inserted + Skipped + Rejected;
}
