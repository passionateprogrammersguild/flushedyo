namespace FlushedYo.Core.Store;

/// <summary>
/// Values recorded alongside a transaction for auditability (plan §5 step 6). The store cannot
/// derive these itself, so the caller supplies them with every insert.
/// </summary>
/// <param name="CanonicalHash">
/// The transaction's canonical hash. Must equal the hash computed from the transaction itself;
/// the store rejects a mismatch.
/// </param>
/// <param name="PluginVersion">Version of the plugin that downloaded the transaction.</param>
/// <param name="DownloadedAt">When the transaction was downloaded. Stored as a UTC instant.</param>
public sealed record TransactionAudit(string CanonicalHash, string PluginVersion, DateTimeOffset DownloadedAt);
