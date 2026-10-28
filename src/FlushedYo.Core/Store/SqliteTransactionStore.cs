using System.Globalization;
using FlushedYo.Core.Models;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Store;

/// <summary>
/// <see cref="ITransactionStore"/> backed by the SQLite ledger. Every call opens and disposes its
/// own connection, so the store is thread-safe. All SQL is parameterized.
/// </summary>
public sealed class SqliteTransactionStore(LedgerDatabase database) : ITransactionStore
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <inheritdoc />
    public async Task<bool> HasTransactionAsync(string canonicalHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalHash);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM transactions WHERE canonical_hash = $hash LIMIT 1;";
        command.Parameters.AddWithValue("$hash", canonicalHash);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <inheritdoc />
    public async Task<bool> RecordTransactionAsync(
        Transaction transaction,
        TransactionAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentException.ThrowIfNullOrWhiteSpace(audit.PluginVersion);

        // Enforces the idempotency invariant at the storage boundary. ComputeHash also rejects
        // invalid transactions; neither failure message includes field values.
        if (!string.Equals(audit.CanonicalHash, CanonicalTransactionHasher.ComputeHash(transaction), StringComparison.Ordinal))
        {
            throw new ArgumentException("The audit hash does not match the transaction.", nameof(audit));
        }

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO transactions
                (canonical_hash, institution, external_id, amount, date, payee, type, plugin_version, downloaded_at)
            VALUES
                ($hash, $institution, $externalId, $amount, $date, $payee, $type, $pluginVersion, $downloadedAt);
            """;
        command.Parameters.AddWithValue("$hash", audit.CanonicalHash);
        command.Parameters.AddWithValue("$institution", transaction.Institution);
        command.Parameters.AddWithValue("$externalId", transaction.ExternalId);
        command.Parameters.AddWithValue("$amount", transaction.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$date", transaction.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$payee", transaction.Payee);
        command.Parameters.AddWithValue("$type", transaction.Type);
        command.Parameters.AddWithValue("$pluginVersion", audit.PluginVersion);
        command.Parameters.AddWithValue("$downloadedAt", audit.DownloadedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT institution, external_id, amount, date, payee, type FROM transactions ORDER BY id;";

        var transactions = new List<Transaction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            transactions.Add(new Transaction
            {
                Institution = reader.GetString(0),
                ExternalId = reader.GetString(1),
                Amount = decimal.Parse(reader.GetString(2), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                Date = DateOnly.ParseExact(reader.GetString(3), DateFormat, CultureInfo.InvariantCulture),
                Payee = reader.GetString(4),
                Type = reader.GetString(5),
            });
        }

        return transactions;
    }
}
