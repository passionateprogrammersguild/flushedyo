using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;
using static FlushedYo.Core.Tests.Store.AccountLedgerTestData;
using static FlushedYo.Core.Tests.Store.SqliteErrorCodes;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Direct database checks for invalid persisted cash-state combinations.</summary>
public sealed class LedgerMigrationV3ConstraintTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("account_imports")]
    [InlineData("account_progress")]
    public async Task InvalidCashStateCombination_IsRejectedByDatabaseCheck(string table)
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var account = Account();
        var imported = await store.ImportAsync(Import(account, [AccountTx(account: account)]), Token);
        await using var connection = await ledger.Database.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "account_imports" => "UPDATE account_imports SET cash_reason = 'missing-baseline';",
            "account_progress" => "UPDATE account_progress SET cash_reason = 'missing-baseline';",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        var exception = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(Token));

        Assert.Equal(Constraint, exception.SqliteErrorCode);
        Assert.Equal(ConstraintCheck, exception.SqliteExtendedErrorCode);
        // The CHECK names are defined by LedgerMigrations here in this repo, so they are a
        // self-defined migration contract, not SQLite incidental message text.
        Assert.Contains(
            table == "account_imports" ? "ck_account_imports_cash_state" : "ck_account_progress_cash_state",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(imported.CashState, await store.GetCashStateAsync(account, Token));
        Assert.Equal(imported.CashState, Assert.Single(await store.GetImportHistoryAsync(account, Token)).CashState);
    }
}
