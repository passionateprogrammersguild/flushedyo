using System.Globalization;
using FlushedYo.Core.Models;
using FlushedYo.Core.Store;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Builders and raw persistence checks for the account-scoped ledger.</summary>
internal static class AccountLedgerTestData
{
    public static Account Account(string institution = "fidelity", string accountKey = "account-1") =>
        new() { Institution = institution, AccountKey = accountKey };

    public static AccountTransaction AccountTx(
        Account? account = null,
        string externalId = "abc-123",
        decimal amount = -12.30m,
        DateOnly? date = null,
        string payee = "Corner Store",
        string type = "debit",
        CashImpact cashImpact = CashImpact.CashAffecting,
        string? transactionInstitution = null)
    {
        account ??= Account();
        return new AccountTransaction
        {
            Account = account,
            Transaction = TransactionTestData.Tx(
                institution: transactionInstitution ?? account.Institution,
                externalId: externalId,
                amount: amount,
                date: date,
                payee: payee,
                type: type),
            CashImpact = cashImpact,
        };
    }

    public static AccountImport Import(
        Account account,
        IReadOnlyCollection<AccountTransaction> transactions,
        decimal? openingCashBalance = 100m,
        int rejected = 0,
        DateTimeOffset? importedAt = null) =>
        new()
        {
            Account = account,
            Transactions = transactions,
            PluginVersion = "1.0.0",
            ImportedAt = importedAt ?? TransactionTestData.DefaultTime,
            OpeningCashBalance = openingCashBalance,
            Rejected = rejected,
        };

    public static SqliteAccountLedgerStore CreateStore(TempLedger ledger) => new(ledger.Database);

    public static async Task<long> ScalarAsync(TempLedger ledger, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await ledger.Database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
