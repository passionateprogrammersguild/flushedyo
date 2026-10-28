using System.Globalization;
using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Shared builders and raw-row readers for store tests.</summary>
internal static class TransactionTestData
{
    public static readonly DateTimeOffset DefaultTime = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    public static Transaction Tx(
        string institution = "fidelity",
        string externalId = "abc-123",
        decimal amount = -12.30m,
        DateOnly? date = null,
        string payee = "Corner Store",
        string type = "debit") =>
        new()
        {
            Institution = institution,
            ExternalId = externalId,
            Amount = amount,
            Date = date ?? new DateOnly(2026, 1, 2),
            Payee = payee,
            Type = type,
        };

    public static TransactionAudit Audit(Transaction transaction, string pluginVersion = "1.0.0", DateTimeOffset? at = null) =>
        new(CanonicalTransactionHasher.ComputeHash(transaction), pluginVersion, at ?? DefaultTime);

    public static SqliteTransactionStore CreateStore(TempLedger ledger) => new(ledger.Database);

    public static async Task<long> CountRowsAsync(TempLedger ledger, CancellationToken cancellationToken)
    {
        await using var connection = await ledger.Database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM transactions;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public static async Task<List<StoredRow>> ReadRowsAsync(TempLedger ledger, CancellationToken cancellationToken)
    {
        await using var connection = await ledger.Database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT canonical_hash, amount, date, payee, plugin_version, downloaded_at FROM transactions ORDER BY id;";

        var rows = new List<StoredRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return rows;
    }

    internal sealed record StoredRow(
        string CanonicalHash,
        string Amount,
        string Date,
        string Payee,
        string PluginVersion,
        string DownloadedAt);
}
