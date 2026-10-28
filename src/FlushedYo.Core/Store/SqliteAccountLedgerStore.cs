using System.Globalization;
using FlushedYo.Core.Models;
using FlushedYo.Core.Tracking;
using Microsoft.Data.Sqlite;

namespace FlushedYo.Core.Store;

/// <summary>
/// SQLite-backed account ledger. Each call owns its connection, every value is parameterized, and an
/// import's rows, history, safe-date watermark, and cash checkpoint share one immediate transaction.
/// </summary>
public sealed class SqliteAccountLedgerStore(LedgerDatabase database) : IAccountLedgerStore
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string AmbiguousSourceMessage =
        "An account transaction source identity is ambiguous; the import was not recorded.";

    /// <inheritdoc />
    public async Task<AccountImportResult> ImportAsync(AccountImport import, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = Prepare(import);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);
        var accountId = await EnsureAccountAsync(connection, transaction, prepared, cancellationToken).ConfigureAwait(false);
        var previous = await ReadProgressAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        var inserted = new List<AccountTransaction>();
        var skipped = 0;
        foreach (var item in prepared.Transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await InsertTransactionAsync(connection, transaction, accountId, item, prepared, cancellationToken).ConfigureAwait(false))
            {
                inserted.Add(item.Value);
            }
            else
            {
                skipped++;
                await VerifyDuplicateAsync(connection, transaction, item, cancellationToken).ConfigureAwait(false);
            }
        }

        var cashState = CalculateCashState(prepared, previous, inserted);
        var latestSafeDate = CalculateLatestSafeDate(prepared, previous);
        var importId = await InsertImportAsync(
            connection,
            transaction,
            accountId,
            prepared,
            inserted.Count,
            skipped,
            latestSafeDate,
            cashState,
            cancellationToken).ConfigureAwait(false);
        await UpsertProgressAsync(
            connection,
            transaction,
            accountId,
            importId,
            prepared.ImportedAt,
            latestSafeDate,
            cashState,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return new AccountImportResult
        {
            Inserted = inserted.Count,
            Skipped = skipped,
            Rejected = prepared.Rejected,
            NewlyInsertedTransactions = inserted,
            LatestSafeDate = latestSafeDate,
            CashState = cashState,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountTransaction>> GetTransactionsAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = AccountTransactionHasher.NormalizeAccount(account);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.institution, t.external_id, t.amount, t.date, t.payee, t.type, t.cash_impact
            FROM account_transactions AS t
            INNER JOIN accounts AS a ON a.id = t.account_id
            WHERE a.institution = $institution AND a.account_key = $accountKey
            ORDER BY t.id;
            """;
        command.Parameters.AddWithValue("$institution", normalized.Institution);
        command.Parameters.AddWithValue("$accountKey", normalized.AccountKey);

        var transactions = new List<AccountTransaction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            transactions.Add(new AccountTransaction
            {
                Account = normalized,
                Transaction = new Transaction
                {
                    Institution = reader.GetString(0),
                    ExternalId = reader.GetString(1),
                    Amount = ParseDecimal(reader.GetString(2)),
                    Date = DateOnly.ParseExact(reader.GetString(3), DateFormat, CultureInfo.InvariantCulture),
                    Payee = reader.GetString(4),
                    Type = reader.GetString(5),
                },
                CashImpact = ParseCashImpact(reader.GetString(6)),
            });
        }

        return transactions;
    }

    /// <inheritdoc />
    public async Task<DateOnly?> GetLatestSafeDateAsync(Account account, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = AccountTransactionHasher.NormalizeAccount(account);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.latest_safe_date
            FROM account_progress AS p
            INNER JOIN accounts AS a ON a.id = p.account_id
            WHERE a.institution = $institution AND a.account_key = $accountKey;
            """;
        command.Parameters.AddWithValue("$institution", normalized.Institution);
        command.Parameters.AddWithValue("$accountKey", normalized.AccountKey);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : DateOnly.ParseExact((string)value, DateFormat, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<CashState?> GetCashStateAsync(Account account, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = AccountTransactionHasher.NormalizeAccount(account);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.cash_status, p.cash_reason, p.opening_cash_balance, p.cash_balance, p.cash_difference
            FROM account_progress AS p
            INNER JOIN accounts AS a ON a.id = p.account_id
            WHERE a.institution = $institution AND a.account_key = $accountKey;
            """;
        command.Parameters.AddWithValue("$institution", normalized.Institution);
        command.Parameters.AddWithValue("$accountKey", normalized.AccountKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadCashState(reader, statusColumn: 0);
    }

    private static PreparedImport Prepare(AccountImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(import.Account);
        ArgumentNullException.ThrowIfNull(import.Transactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(import.PluginVersion);
        if (import.Rejected < 0)
        {
            throw new ArgumentException("The rejected count must not be negative.", nameof(import));
        }

        var account = AccountTransactionHasher.NormalizeAccount(import.Account);
        var transactions = new List<PreparedTransaction>(import.Transactions.Count);
        foreach (var value in import.Transactions)
        {
            if (value is null)
            {
                throw new ArgumentException("Invalid account transaction: transaction is null.", nameof(import));
            }

            if (!AccountTransactionHasher.TryValidate(value, out var error))
            {
                throw new ArgumentException($"Invalid account transaction: {error}.", nameof(import));
            }

            var itemAccount = AccountTransactionHasher.NormalizeAccount(value.Account);
            if (!string.Equals(account.Institution, itemAccount.Institution, StringComparison.Ordinal) ||
                !string.Equals(account.AccountKey, itemAccount.AccountKey, StringComparison.Ordinal))
            {
                throw new ArgumentException("A transaction belongs to a different account.", nameof(import));
            }

            var normalized = value with { Account = itemAccount };
            transactions.Add(new PreparedTransaction(
                normalized,
                AccountTransactionHasher.ComputeHash(normalized),
                CanonicalTransactionHasher.ComputeHash(normalized.Transaction)));
        }

        return new PreparedImport(
            account,
            transactions,
            import.PluginVersion,
            import.ImportedAt,
            import.OpeningCashBalance,
            import.Rejected);
    }

    private static async Task<long> EnsureAccountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PreparedImport import,
        CancellationToken cancellationToken)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT OR IGNORE INTO accounts (institution, account_key, created_at)
                VALUES ($institution, $accountKey, $createdAt);
                """;
            insert.Parameters.AddWithValue("$institution", import.Account.Institution);
            insert.Parameters.AddWithValue("$accountKey", import.Account.AccountKey);
            insert.Parameters.AddWithValue("$createdAt", FormatTimestamp(import.ImportedAt));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM accounts WHERE institution = $institution AND account_key = $accountKey;";
        select.Parameters.AddWithValue("$institution", import.Account.Institution);
        select.Parameters.AddWithValue("$accountKey", import.Account.AccountKey);
        var id = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return id is null
            ? throw new LedgerException("The account could not be persisted.")
            : Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    private static async Task<AccountProgress?> ReadProgressAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT latest_safe_date, cash_status, cash_reason, opening_cash_balance, cash_balance, cash_difference
            FROM account_progress
            WHERE account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var latestSafeDate = reader.IsDBNull(0)
            ? (DateOnly?)null
            : DateOnly.ParseExact(reader.GetString(0), DateFormat, CultureInfo.InvariantCulture);
        return new AccountProgress(latestSafeDate, ReadCashState(reader, statusColumn: 1));
    }

    private static async Task<bool> InsertTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountId,
        PreparedTransaction item,
        PreparedImport import,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO account_transactions
                (account_id, dedup_hash, legacy_hash, institution, source_key, external_id, amount, date,
                 payee, type, cash_impact, plugin_version, downloaded_at)
            VALUES
                ($accountId, $dedupHash, $legacyHash, $institution, $sourceKey, $externalId, $amount, $date,
                 $payee, $type, $cashImpact, $pluginVersion, $downloadedAt)
            ON CONFLICT(dedup_hash) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$dedupHash", item.DedupHash);
        command.Parameters.AddWithValue("$legacyHash", item.LegacyHash);
        command.Parameters.AddWithValue("$institution", item.Value.Transaction.Institution);
        command.Parameters.AddWithValue("$sourceKey", item.Value.Transaction.ExternalId.Trim());
        command.Parameters.AddWithValue("$externalId", item.Value.Transaction.ExternalId);
        command.Parameters.AddWithValue("$amount", FormatDecimal(item.Value.Transaction.Amount));
        command.Parameters.AddWithValue("$date", item.Value.Transaction.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$payee", item.Value.Transaction.Payee);
        command.Parameters.AddWithValue("$type", item.Value.Transaction.Type);
        command.Parameters.AddWithValue("$cashImpact", ToStorage(item.Value.CashImpact));
        command.Parameters.AddWithValue("$pluginVersion", import.PluginVersion);
        command.Parameters.AddWithValue("$downloadedAt", FormatTimestamp(import.ImportedAt));

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new LedgerException(AmbiguousSourceMessage, exception);
        }
    }

    private static async Task VerifyDuplicateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PreparedTransaction item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT source_key, cash_impact FROM account_transactions WHERE dedup_hash = $dedupHash;";
        command.Parameters.AddWithValue("$dedupHash", item.DedupHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
            !string.Equals(reader.GetString(0), item.Value.Transaction.ExternalId.Trim(), StringComparison.Ordinal) ||
            ParseCashImpact(reader.GetString(1)) != item.Value.CashImpact)
        {
            throw new LedgerException(AmbiguousSourceMessage);
        }
    }

    private static CashState CalculateCashState(
        PreparedImport import,
        AccountProgress? previous,
        List<AccountTransaction> inserted)
    {
        if (previous is not null && inserted.Count == 0 && import.Rejected == 0)
        {
            // Replaying an old baseline with no new rows must not rewind or double-apply progress.
            return previous.CashState;
        }

        var hasUnresolvedNewActivity = inserted.Any(static item => item.CashImpact is
            CashImpact.Unknown or CashImpact.Correction or CashImpact.Unsupported);
        if (import.OpeningCashBalance is null &&
            import.Rejected == 0 &&
            !hasUnresolvedNewActivity &&
            previous?.CashState is { Status: not CashStateStatus.Known })
        {
            // New clean activity cannot repair an earlier unknown or reconciliation-needed state by itself.
            return previous.CashState;
        }

        var opening = import.OpeningCashBalance;
        if (opening is null && previous?.CashState is { Status: CashStateStatus.Known } known)
        {
            opening = known.CashBalance;
        }

        return CashStateCalculator.Calculate(opening, inserted, import.Rejected > 0);
    }

    private static DateOnly? CalculateLatestSafeDate(PreparedImport import, AccountProgress? previous)
    {
        var latest = previous?.LatestSafeDate;
        if (import.Rejected > 0 || import.Transactions.Count == 0)
        {
            return latest;
        }

        var candidate = import.Transactions.Max(static item => item.Value.Transaction.Date);
        return latest is null || candidate > latest ? candidate : latest;
    }

    private static async Task<long> InsertImportAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountId,
        PreparedImport import,
        int inserted,
        int skipped,
        DateOnly? latestSafeDate,
        CashState cashState,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO account_imports
                (account_id, plugin_version, imported_at, inserted, skipped, rejected, latest_safe_date,
                 cash_status, cash_reason, opening_cash_balance, cash_balance, cash_difference)
            VALUES
                ($accountId, $pluginVersion, $importedAt, $inserted, $skipped, $rejected, $latestSafeDate,
                 $cashStatus, $cashReason, $openingCashBalance, $cashBalance, $cashDifference)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$pluginVersion", import.PluginVersion);
        command.Parameters.AddWithValue("$importedAt", FormatTimestamp(import.ImportedAt));
        command.Parameters.AddWithValue("$inserted", inserted);
        command.Parameters.AddWithValue("$skipped", skipped);
        command.Parameters.AddWithValue("$rejected", import.Rejected);
        AddNullable(command, "$latestSafeDate", latestSafeDate?.ToString(DateFormat, CultureInfo.InvariantCulture));
        AddCashStateParameters(command, cashState);

        var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    private static async Task UpsertProgressAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long accountId,
        long importId,
        DateTimeOffset importedAt,
        DateOnly? latestSafeDate,
        CashState cashState,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO account_progress
                (account_id, last_import_id, latest_safe_date, cash_status, cash_reason,
                 opening_cash_balance, cash_balance, cash_difference, updated_at)
            VALUES
                ($accountId, $lastImportId, $latestSafeDate, $cashStatus, $cashReason,
                 $openingCashBalance, $cashBalance, $cashDifference, $updatedAt)
            ON CONFLICT(account_id) DO UPDATE SET
                last_import_id = $lastImportId,
                latest_safe_date = $latestSafeDate,
                cash_status = $cashStatus,
                cash_reason = $cashReason,
                opening_cash_balance = $openingCashBalance,
                cash_balance = $cashBalance,
                cash_difference = $cashDifference,
                updated_at = $updatedAt;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$lastImportId", importId);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(importedAt));
        AddNullable(command, "$latestSafeDate", latestSafeDate?.ToString(DateFormat, CultureInfo.InvariantCulture));
        AddCashStateParameters(command, cashState);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCashStateParameters(SqliteCommand command, CashState cashState)
    {
        command.Parameters.AddWithValue("$cashStatus", ToStorage(cashState.Status));
        command.Parameters.AddWithValue("$cashReason", ToStorage(cashState.Reason));
        AddNullable(command, "$openingCashBalance", cashState.OpeningCashBalance is null ? null : FormatDecimal(cashState.OpeningCashBalance.Value));
        AddNullable(command, "$cashBalance", cashState.CashBalance is null ? null : FormatDecimal(cashState.CashBalance.Value));
        AddNullable(command, "$cashDifference", cashState.Difference is null ? null : FormatDecimal(cashState.Difference.Value));
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static CashState ReadCashState(SqliteDataReader reader, int statusColumn)
    {
        var status = ParseCashStatus(reader.GetString(statusColumn));
        var reason = ParseCashReason(reader.GetString(statusColumn + 1));
        return new CashState(
            status,
            reason,
            ReadNullableDecimal(reader, statusColumn + 2),
            ReadNullableDecimal(reader, statusColumn + 3),
            ReadNullableDecimal(reader, statusColumn + 4));
    }

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : ParseDecimal(reader.GetString(column));

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string ToStorage(CashImpact value) => value switch
    {
        CashImpact.CashAffecting => "cash",
        CashImpact.NotCashAffecting => "non-cash",
        CashImpact.Unknown => "unknown",
        CashImpact.Correction => "correction",
        CashImpact.Unsupported => "unsupported",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static CashImpact ParseCashImpact(string value) => value switch
    {
        "cash" => CashImpact.CashAffecting,
        "non-cash" => CashImpact.NotCashAffecting,
        "unknown" => CashImpact.Unknown,
        "correction" => CashImpact.Correction,
        "unsupported" => CashImpact.Unsupported,
        _ => throw new LedgerException("The ledger contains an unknown cash impact."),
    };

    private static string ToStorage(CashStateStatus value) => value switch
    {
        CashStateStatus.Known => "known",
        CashStateStatus.Unknown => "unknown",
        CashStateStatus.ReconciliationNeeded => "reconciliation-needed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static CashStateStatus ParseCashStatus(string value) => value switch
    {
        "known" => CashStateStatus.Known,
        "unknown" => CashStateStatus.Unknown,
        "reconciliation-needed" => CashStateStatus.ReconciliationNeeded,
        _ => throw new LedgerException("The ledger contains an unknown cash-state status."),
    };

    private static string ToStorage(CashStateReason value) => value switch
    {
        CashStateReason.None => "none",
        CashStateReason.MissingBaseline => "missing-baseline",
        CashStateReason.AmbiguousTransaction => "ambiguous-transaction",
        CashStateReason.Correction => "correction",
        CashStateReason.UnsupportedActivity => "unsupported-activity",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static CashStateReason ParseCashReason(string value) => value switch
    {
        "none" => CashStateReason.None,
        "missing-baseline" => CashStateReason.MissingBaseline,
        "ambiguous-transaction" => CashStateReason.AmbiguousTransaction,
        "correction" => CashStateReason.Correction,
        "unsupported-activity" => CashStateReason.UnsupportedActivity,
        _ => throw new LedgerException("The ledger contains an unknown cash-state reason."),
    };

    private sealed record PreparedImport(
        Account Account,
        IReadOnlyList<PreparedTransaction> Transactions,
        string PluginVersion,
        DateTimeOffset ImportedAt,
        decimal? OpeningCashBalance,
        int Rejected);

    private sealed record PreparedTransaction(AccountTransaction Value, string DedupHash, string LegacyHash);

    private sealed record AccountProgress(DateOnly? LatestSafeDate, CashState CashState);
}
