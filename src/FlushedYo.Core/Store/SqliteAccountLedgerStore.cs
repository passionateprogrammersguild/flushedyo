using System.Globalization;
using System.Collections.Frozen;
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

    // SQLite result codes from sqlite3.h: extended codes encode as primary | (kind << 8), so
    // SQLITE_CONSTRAINT_UNIQUE is 19 | (8 << 8) == 2067 — a fixed, documented encoding, unlike the
    // error-message text, which is not a stable contract. Microsoft.Data.Sqlite always populates
    // SqliteException.SqliteExtendedErrorCode via sqlite3_extended_errcode.
    private const int SqliteConstraintError = 19;
    private const int SqliteUniqueConstraintExtendedError = 2067;

    private readonly Action? afterTransactionInsert;

    internal SqliteAccountLedgerStore(LedgerDatabase database, Action? afterTransactionInsert)
        : this(database)
    {
        this.afterTransactionInsert = afterTransactionInsert;
    }

    /// <inheritdoc />
    public async Task<AccountImportResult> ImportAsync(AccountImport import, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = Prepare(import);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);
        var scope = new LedgerScope(connection, transaction, AccountId: 0);
        var accountId = await EnsureAccountAsync(scope, prepared, cancellationToken).ConfigureAwait(false);
        scope = scope with { AccountId = accountId };
        var previous = await ReadProgressAsync(scope, cancellationToken).ConfigureAwait(false);

        var inserted = new List<AccountTransaction>();
        var skipped = 0;
        await using var insertCommand = CreateInsertCommand(scope, prepared);
        await using var duplicateCommand = CreateDuplicateCommand(scope);
        foreach (var item in prepared.Transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await InsertTransactionAsync(scope, insertCommand, item, cancellationToken).ConfigureAwait(false))
            {
                inserted.Add(item.Value);
                afterTransactionInsert?.Invoke();
            }
            else
            {
                skipped++;
                await VerifyDuplicateAsync(duplicateCommand, item, cancellationToken).ConfigureAwait(false);
            }
        }

        var cashState = CalculateCashState(prepared, previous, inserted);
        var latestSafeDate = CalculateLatestSafeDate(prepared, previous);
        var checkpoint = new CashCheckpoint(latestSafeDate, cashState);
        var importId = await InsertImportAsync(
            scope,
            prepared,
            new ImportCounts(inserted.Count, skipped),
            checkpoint,
            cancellationToken).ConfigureAwait(false);
        await UpsertProgressAsync(scope, importId, prepared.ImportedAt, checkpoint, cancellationToken).ConfigureAwait(false);

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
    public Task<IReadOnlyList<AccountImportRecord>> GetImportHistoryAsync(
        Account account,
        CancellationToken cancellationToken = default) =>
        ReadImportHistoryAsync(account, maximumRecords: null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountImportRecord>> GetImportHistoryAsync(
        Account account,
        int maximumRecords,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        return ReadImportHistoryAsync(account, maximumRecords, cancellationToken);
    }

    private async Task<IReadOnlyList<AccountImportRecord>> ReadImportHistoryAsync(
        Account account,
        int? maximumRecords,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = AccountTransactionHasher.NormalizeAccount(account);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = maximumRecords is null
            ? """
              SELECT i.plugin_version, i.imported_at, i.inserted, i.skipped, i.rejected, i.latest_safe_date,
                     i.cash_status, i.cash_reason, i.opening_cash_balance, i.cash_balance, i.cash_difference
              FROM account_imports AS i
              INNER JOIN accounts AS a ON a.id = i.account_id
              WHERE a.institution = $institution AND a.account_key = $accountKey
              ORDER BY i.id;
              """
            : """
              SELECT i.plugin_version, i.imported_at, i.inserted, i.skipped, i.rejected, i.latest_safe_date,
                     i.cash_status, i.cash_reason, i.opening_cash_balance, i.cash_balance, i.cash_difference
              FROM account_imports AS i
              INNER JOIN accounts AS a ON a.id = i.account_id
              WHERE a.institution = $institution AND a.account_key = $accountKey
              ORDER BY i.id DESC
              LIMIT $maximumRecords;
              """;
        command.Parameters.AddWithValue("$institution", normalized.Institution);
        command.Parameters.AddWithValue("$accountKey", normalized.AccountKey);
        if (maximumRecords is not null)
        {
            command.Parameters.AddWithValue("$maximumRecords", maximumRecords.Value);
        }

        var history = new List<AccountImportRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            history.Add(new AccountImportRecord
            {
                PluginVersion = reader.GetString(0),
                ImportedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                Inserted = reader.GetInt32(2),
                Skipped = reader.GetInt32(3),
                Rejected = reader.GetInt32(4),
                LatestSafeDate = reader.IsDBNull(5)
                    ? null
                    : DateOnly.ParseExact(reader.GetString(5), DateFormat, CultureInfo.InvariantCulture),
                CashState = ReadCashState(reader, statusColumn: 6),
            });
        }

        if (maximumRecords is not null)
        {
            history.Reverse();
        }

        return history;
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

    /// <summary>
    /// Opens a connection and builds a command against <c>account_progress</c> for the normalized
    /// account; the SQL selects columns from the progress row joined through <c>accounts</c>.
    /// </summary>
    private async Task<ProgressRead> CreateProgressReadAsync(string sql, Account account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = AccountTransactionHasher.NormalizeAccount(account);

        var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$institution", normalized.Institution);
        command.Parameters.AddWithValue("$accountKey", normalized.AccountKey);
        return new ProgressRead(connection, command);
    }

    private sealed record ProgressRead(SqliteConnection Connection, SqliteCommand Command) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Command.DisposeAsync().ConfigureAwait(false);
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<DateOnly?> GetLatestSafeDateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var read = await CreateProgressReadAsync(
            """
            SELECT p.latest_safe_date
            FROM account_progress AS p
            INNER JOIN accounts AS a ON a.id = p.account_id
            WHERE a.institution = $institution AND a.account_key = $accountKey;
            """,
            account,
            cancellationToken).ConfigureAwait(false);

        var value = await read.Command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : DateOnly.ParseExact((string)value, DateFormat, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<CashState?> GetCashStateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var read = await CreateProgressReadAsync(
            """
            SELECT p.cash_status, p.cash_reason, p.opening_cash_balance, p.cash_balance, p.cash_difference
            FROM account_progress AS p
            INNER JOIN accounts AS a ON a.id = p.account_id
            WHERE a.institution = $institution AND a.account_key = $accountKey;
            """,
            account,
            cancellationToken).ConfigureAwait(false);

        await using var reader = await read.Command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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

            var itemAccount = AccountTransactionHasher.NormalizeValidatedAccount(value.Account);
            if (!string.Equals(account.Institution, itemAccount.Institution, StringComparison.Ordinal) ||
                !string.Equals(account.AccountKey, itemAccount.AccountKey, StringComparison.Ordinal))
            {
                throw new ArgumentException("A transaction belongs to a different account.", nameof(import));
            }

            var normalized = value with { Account = itemAccount };
            var legacyHash = CanonicalTransactionHasher.ComputeHash(normalized.Transaction);
            transactions.Add(new PreparedTransaction(
                normalized,
                AccountTransactionHasher.ComputeHashFromLegacyHash(normalized, legacyHash),
                legacyHash,
                normalized.Transaction.ExternalId.Trim()));
        }

        return new PreparedImport(
            account,
            transactions,
            import.PluginVersion,
            import.ImportedAt,
            import.OpeningCashBalance,
            import.Rejected);
    }

    private static async Task<long> EnsureAccountAsync(LedgerScope scope, PreparedImport import, CancellationToken cancellationToken)
    {
        var connection = scope.Connection;
        var transaction = scope.Transaction;
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

    private static async Task<AccountProgress?> ReadProgressAsync(LedgerScope scope, CancellationToken cancellationToken)
    {
        await using var command = scope.Connection.CreateCommand();
        command.Transaction = scope.Transaction;
        command.CommandText =
            """
            SELECT latest_safe_date, cash_status, cash_reason, opening_cash_balance, cash_balance, cash_difference
            FROM account_progress
            WHERE account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", scope.AccountId);
        {
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
    }

    private static SqliteCommand CreateInsertCommand(LedgerScope scope, PreparedImport import)
    {
        var command = scope.Connection.CreateCommand();
        command.Transaction = scope.Transaction;
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
        command.Parameters.AddWithValue("$accountId", scope.AccountId);
        command.Parameters.Add("$dedupHash", SqliteType.Text);
        command.Parameters.Add("$legacyHash", SqliteType.Text);
        command.Parameters.Add("$institution", SqliteType.Text);
        command.Parameters.Add("$sourceKey", SqliteType.Text);
        command.Parameters.Add("$externalId", SqliteType.Text);
        command.Parameters.Add("$amount", SqliteType.Text);
        command.Parameters.Add("$date", SqliteType.Text);
        command.Parameters.Add("$payee", SqliteType.Text);
        command.Parameters.Add("$type", SqliteType.Text);
        command.Parameters.Add("$cashImpact", SqliteType.Text);
        command.Parameters.AddWithValue("$pluginVersion", import.PluginVersion);
        command.Parameters.AddWithValue("$downloadedAt", FormatTimestamp(import.ImportedAt));
        return command;
    }

    private static SqliteCommand CreateDuplicateCommand(LedgerScope scope)
    {
        var command = scope.Connection.CreateCommand();
        command.Transaction = scope.Transaction;
        command.CommandText = "SELECT source_key, cash_impact FROM account_transactions WHERE dedup_hash = $dedupHash;";
        command.Parameters.Add("$dedupHash", SqliteType.Text);
        return command;
    }

    private static async Task<bool> InsertTransactionAsync(
        LedgerScope scope,
        SqliteCommand command,
        PreparedTransaction item,
        CancellationToken cancellationToken)
    {
        command.Parameters["$dedupHash"].Value = item.DedupHash;
        command.Parameters["$legacyHash"].Value = item.LegacyHash;
        command.Parameters["$institution"].Value = item.Value.Transaction.Institution;
        command.Parameters["$sourceKey"].Value = item.SourceKey;
        command.Parameters["$externalId"].Value = item.Value.Transaction.ExternalId;
        command.Parameters["$amount"].Value = FormatDecimal(item.Value.Transaction.Amount);
        command.Parameters["$date"].Value = item.Value.Transaction.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
        command.Parameters["$payee"].Value = item.Value.Transaction.Payee;
        command.Parameters["$type"].Value = item.Value.Transaction.Type;
        command.Parameters["$cashImpact"].Value = ToStorage(item.Value.CashImpact);

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode == SqliteConstraintError &&
            exception.SqliteExtendedErrorCode == SqliteUniqueConstraintExtendedError)
        {
            // Restricting the filter to SQLITE_CONSTRAINT_UNIQUE keeps unrelated constraint kinds —
            // e.g. a RAISE(ABORT) trigger's extended code — surfacing raw even when same-source data
            // already exists for the account.
            if (await HasSourceIdentityRowAsync(scope, item.SourceKey, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new LedgerException(AmbiguousSourceMessage, exception);
            }

            // A unique index other than the source identity fired; do not misreport it.
            throw;
        }
    }

    private static async Task<bool> HasSourceIdentityRowAsync(LedgerScope scope, string sourceKey, CancellationToken cancellationToken)
    {
        // Under SQLite's default ABORT resolution the failed statement rolled back while this
        // transaction stays usable, so this probe classifies a conflict that already happened; it is
        // not check-then-insert, and the atomic ON CONFLICT(dedup_hash) DO NOTHING result remains the
        // source of truth for dedup.
        await using var probe = scope.Connection.CreateCommand();
        probe.Transaction = scope.Transaction;
        probe.CommandText =
            """
            SELECT 1 FROM account_transactions WHERE account_id = $accountId AND source_key = $sourceKey;
            """;
        probe.Parameters.AddWithValue("$accountId", scope.AccountId);
        probe.Parameters.AddWithValue("$sourceKey", sourceKey);
        var row = await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return row is not null and not DBNull;
    }

    private static async Task VerifyDuplicateAsync(
        SqliteCommand command,
        PreparedTransaction item,
        CancellationToken cancellationToken)
    {
        command.Parameters["$dedupHash"].Value = item.DedupHash;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var read = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read || !MatchesOriginal(reader, item))
        {
            throw new LedgerException(AmbiguousSourceMessage);
        }
    }

    private static bool MatchesOriginal(SqliteDataReader reader, PreparedTransaction item) =>
        string.Equals(reader.GetString(0), item.SourceKey, StringComparison.Ordinal) &&
        ParseCashImpact(reader.GetString(1)) == item.Value.CashImpact;

    private static CashState CalculateCashState(
        PreparedImport import,
        AccountProgress? previous,
        List<AccountTransaction> inserted)
    {
        var hasUnresolvedNewActivity = inserted.Any(static item => item.CashImpact is
            CashImpact.Unknown or CashImpact.Correction or CashImpact.Unsupported);
        if (previous is { } progress && IsDuplicateOnlyResync(import, progress, inserted.Count))
        {
            // A duplicate-only import without a new baseline cannot advance or double-apply progress.
            return progress.CashState;
        }

        if (previous is { } unrepairable && CannotRepairUnresolvedState(import, unrepairable, hasUnresolvedNewActivity))
        {
            // New clean activity cannot repair an earlier unknown or reconciliation-needed state by itself.
            return unrepairable.CashState;
        }

        return ResolveOpening(import, previous, inserted);
    }

    private static bool IsDuplicateOnlyResync(PreparedImport import, AccountProgress previous, int insertedCount) =>
        insertedCount == 0 && import.Rejected == 0 && import.OpeningCashBalance is null;

    private static bool CannotRepairUnresolvedState(
        PreparedImport import,
        AccountProgress previous,
        bool hasUnresolvedNewActivity) =>
        import.OpeningCashBalance is null &&
        import.Rejected == 0 &&
        !hasUnresolvedNewActivity &&
        previous.CashState is { Status: not CashStateStatus.Known };

    private static CashState ResolveOpening(PreparedImport import, AccountProgress? previous, List<AccountTransaction> inserted)
    {
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
        LedgerScope scope,
        PreparedImport import,
        ImportCounts counts,
        CashCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var command = scope.Connection.CreateCommand();
        command.Transaction = scope.Transaction;
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
        command.Parameters.AddWithValue("$accountId", scope.AccountId);
        command.Parameters.AddWithValue("$pluginVersion", import.PluginVersion);
        command.Parameters.AddWithValue("$importedAt", FormatTimestamp(import.ImportedAt));
        command.Parameters.AddWithValue("$inserted", counts.Inserted);
        command.Parameters.AddWithValue("$skipped", counts.Skipped);
        command.Parameters.AddWithValue("$rejected", import.Rejected);
        AddNullable(command, "$latestSafeDate", checkpoint.LatestSafeDate?.ToString(DateFormat, CultureInfo.InvariantCulture));
        AddCashStateParameters(command, checkpoint.State);

        var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    private static async Task UpsertProgressAsync(
        LedgerScope scope,
        long importId,
        DateTimeOffset importedAt,
        CashCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var command = scope.Connection.CreateCommand();
        command.Transaction = scope.Transaction;
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
        command.Parameters.AddWithValue("$accountId", scope.AccountId);
        command.Parameters.AddWithValue("$lastImportId", importId);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(importedAt));
        AddNullable(command, "$latestSafeDate", checkpoint.LatestSafeDate?.ToString(DateFormat, CultureInfo.InvariantCulture));
        AddCashStateParameters(command, checkpoint.State);
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

    private static string ToStorage(CashImpact value) => EnumStorage<CashImpact>.ToStorage(value);

    private static CashImpact ParseCashImpact(string value) => EnumStorage<CashImpact>.FromStorage(
        value,
        static () => new LedgerException("The ledger contains an unknown cash impact."));

    private static string ToStorage(CashStateStatus value) => EnumStorage<CashStateStatus>.ToStorage(value);

    private static CashStateStatus ParseCashStatus(string value) => EnumStorage<CashStateStatus>.FromStorage(
        value,
        static () => new LedgerException("The ledger contains an unknown cash-state status."));

    private static string ToStorage(CashStateReason value) => EnumStorage<CashStateReason>.ToStorage(value);

    private static CashStateReason ParseCashReason(string value) => EnumStorage<CashStateReason>.FromStorage(
        value,
        static () => new LedgerException("The ledger contains an unknown cash-state reason."));

    /// <summary>Maps an enum to its storage string and back, throwing the caller's exception on unknown text.</summary>
    internal static class EnumStorage<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly FrozenDictionary<TEnum, string> ToStorageMap = BuildMap();

        private static FrozenDictionary<TEnum, string> BuildMap() =>
            typeof(TEnum) == typeof(CashImpact) ? (FrozenDictionary<TEnum, string>)(object)CashImpactMap()
            : typeof(TEnum) == typeof(CashStateStatus) ? (FrozenDictionary<TEnum, string>)(object)CashStateStatusMap
            : typeof(TEnum) == typeof(CashStateReason) ? (FrozenDictionary<TEnum, string>)(object)CashStateReasonMap
            : throw new InvalidOperationException($"No storage map for {typeof(TEnum).Name}.");

        private static FrozenDictionary<CashImpact, string> CashImpactMap() =>
            new Dictionary<CashImpact, string>
            {
                [CashImpact.CashAffecting] = "cash",
                [CashImpact.NotCashAffecting] = "non-cash",
                [CashImpact.Unknown] = "unknown",
                [CashImpact.Correction] = "correction",
                [CashImpact.Unsupported] = "unsupported",
            }.ToFrozenDictionary();

        private static FrozenDictionary<CashStateStatus, string> CashStateStatusMap =>
            new Dictionary<CashStateStatus, string>
            {
                [CashStateStatus.Known] = "known",
                [CashStateStatus.Unknown] = "unknown",
                [CashStateStatus.ReconciliationNeeded] = "reconciliation-needed",
            }.ToFrozenDictionary();

        private static FrozenDictionary<CashStateReason, string> CashStateReasonMap =>
            new Dictionary<CashStateReason, string>
            {
                [CashStateReason.None] = "none",
                [CashStateReason.MissingBaseline] = "missing-baseline",
                [CashStateReason.AmbiguousTransaction] = "ambiguous-transaction",
                [CashStateReason.Correction] = "correction",
                [CashStateReason.UnsupportedActivity] = "unsupported-activity",
                [CashStateReason.ArithmeticOverflow] = "arithmetic-overflow",
            }.ToFrozenDictionary();

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> for unmapped enum values.</summary>
        public static string ToStorage(TEnum value) =>
            ToStorageMap.TryGetValue(value, out var storage)
                ? storage
                : throw new ArgumentOutOfRangeException(nameof(value));

        /// <summary>Parses a storage string, throwing the caller-supplied exception for unknown text.</summary>
        public static TEnum FromStorage(string value, Func<Exception> unknownValue)
        {
            foreach (var (enumValue, storage) in ToStorageMap)
            {
                if (string.Equals(storage, value, StringComparison.Ordinal))
                {
                    return enumValue;
                }
            }

            throw unknownValue();
        }
    }

    private sealed record PreparedImport(
        Account Account,
        IReadOnlyList<PreparedTransaction> Transactions,
        string PluginVersion,
        DateTimeOffset ImportedAt,
        decimal? OpeningCashBalance,
        int Rejected);

    private sealed record PreparedTransaction(
        AccountTransaction Value,
        string DedupHash,
        string LegacyHash,
        string SourceKey);

    private sealed record AccountProgress(DateOnly? LatestSafeDate, CashState CashState);

    private sealed record ImportCounts(int Inserted, int Skipped);

    private sealed record CashCheckpoint(DateOnly? LatestSafeDate, CashState State);

    private sealed record LedgerScope(SqliteConnection Connection, SqliteTransaction Transaction, long AccountId);
}
