using FlushedYo.Core.Models;

namespace FlushedYo.Core.Store;

/// <summary>One fully materialized account import to record atomically.</summary>
public sealed record AccountImport
{
    /// <summary>Account whose transactions and progress are updated.</summary>
    public required Account Account { get; init; }

    /// <summary>Valid downloaded transactions. Duplicates are resolved by the store's atomic insert result.</summary>
    public required IReadOnlyCollection<AccountTransaction> Transactions { get; init; }

    /// <summary>Version of the plugin that supplied the import.</summary>
    public required string PluginVersion { get; init; }

    /// <summary>Import timestamp, stored as a UTC instant.</summary>
    public required DateTimeOffset ImportedAt { get; init; }

    /// <summary>
    /// Opening cash baseline for this import, or <see langword="null"/> to use the previously known cash
    /// balance. A supplied baseline is authoritative for this import's newly inserted rows, including an
    /// empty or duplicate-only import, so it can explicitly resynchronize after reconciliation without
    /// replaying history. It does not override rejected or unresolved activity in the same import. A missing
    /// baseline on the first import produces an unknown state.
    /// </summary>
    public decimal? OpeningCashBalance { get; init; }

    /// <summary>Number of downloaded records rejected before this valid batch was constructed.</summary>
    public int Rejected { get; init; }
}
