using FlushedYo.Core.Models;

namespace FlushedYo.Core.Store;

/// <summary>A readable, account-scoped import history entry.</summary>
public sealed record AccountImportRecord
{
    /// <summary>Version of the plugin that supplied the import.</summary>
    public required string PluginVersion { get; init; }

    /// <summary>UTC instant at which the import was recorded.</summary>
    public required DateTimeOffset ImportedAt { get; init; }

    /// <summary>Number of newly inserted account transactions.</summary>
    public required int Inserted { get; init; }

    /// <summary>Number of valid transactions already present in this account.</summary>
    public required int Skipped { get; init; }

    /// <summary>Number of downloaded records rejected before storage.</summary>
    public required int Rejected { get; init; }

    /// <summary>
    /// Inclusive safe-date watermark after this import. A following download must overlap this date,
    /// never use it as an exclusive boundary.
    /// </summary>
    public required DateOnly? LatestSafeDate { get; init; }

    /// <summary>Cash-only progress after this import. This is not total investment value.</summary>
    public required CashState CashState { get; init; }

    /// <summary>Total downloaded records represented by this history entry.</summary>
    public int Total => Inserted + Skipped + Rejected;
}
