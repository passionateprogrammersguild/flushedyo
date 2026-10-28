using System.Globalization;
using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Version handling for ledger migrations (spec: ledger_database_and_migrations).</summary>
public sealed class LedgerMigrationsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Versions_AreContiguousFromOneAndAscending()
    {
        var versions = LedgerMigrations.All.Select(static migration => migration.Version).ToArray();

        Assert.Equal(Enumerable.Range(1, versions.Length), versions);
        Assert.Equal(versions[^1], LedgerMigrations.LatestVersion);
    }

    [Fact]
    public async Task DatabaseNewerThanThisBuild_ThrowsLedgerException_AndReleasesTheFile()
    {
        using var ledger = new TempLedger();
        await using (var raw = ledger.OpenRawConnection())
        {
            // 99 stands in for "a future version"; it must exceed what this build knows.
            Assert.True(99 > LedgerMigrations.LatestVersion);
            await ExecuteAsync(raw, "PRAGMA user_version = 99;");
        }

        var exception = await Assert.ThrowsAsync<LedgerException>(() => ledger.Database.OpenAsync(Token));

        Assert.Contains("newer", exception.Message, StringComparison.Ordinal);
        File.Delete(ledger.DatabasePath);
        Assert.False(File.Exists(ledger.DatabasePath));
    }

    [Fact]
    public async Task FailedMigration_ThrowsLedgerExceptionWithInnerException_AndLeavesVersionZero()
    {
        using var ledger = new TempLedger();
        await using (var raw = ledger.OpenRawConnection())
        {
            await ExecuteAsync(raw, "CREATE TABLE transactions (conflicting INTEGER);");
        }

        var exception = await Assert.ThrowsAsync<LedgerException>(() => ledger.Database.OpenAsync(Token));

        Assert.NotNull(exception.InnerException);
        await using var check = ledger.OpenRawConnection();
        Assert.Equal(0, await UserVersionAsync(check));
    }

    [Fact]
    public async Task Apply_OnCurrentDatabase_IsANoOpAndKeepsData()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);
        await ExecuteAsync(
            connection,
            "INSERT INTO transactions (canonical_hash, institution, external_id, amount, date, payee, type, plugin_version, downloaded_at) " +
            "VALUES ('h', 'fidelity', '1', '5.00', '2026-01-01', 'Test', 'debit', '1.0.0', '2026-01-01T00:00:00.0000000Z');");

        LedgerMigrations.Apply(connection);

        Assert.Equal(LedgerMigrations.LatestVersion, await UserVersionAsync(connection));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM transactions;";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<int> UserVersionAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
    }
}
