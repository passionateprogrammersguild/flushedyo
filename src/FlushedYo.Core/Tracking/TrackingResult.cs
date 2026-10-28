namespace FlushedYo.Core.Tracking;

/// <summary>Outcome of tracking one batch of transactions.</summary>
/// <param name="Inserted">New rows written to the ledger.</param>
/// <param name="Skipped">
/// Transactions whose canonical hash already existed, including duplicates within the same batch.
/// </param>
/// <param name="Rejected">Transactions that failed validation and were not stored.</param>
public sealed record TrackingResult(int Inserted, int Skipped, int Rejected)
{
    /// <summary>Total number of transactions examined.</summary>
    public int Total => Inserted + Skipped + Rejected;
}
