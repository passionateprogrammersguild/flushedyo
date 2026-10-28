using System.Data;
using FlushedYo.Core.Configuration;
using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Lazy open, schema creation, and idempotency (spec: ledger_database_and_migrations).</summary>
public sealed class LedgerDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Construction_DoesNotTouchTheDisk()
    {
        using var ledger = new TempLedger();

        Assert.False(Directory.Exists(ledger.DirectoryPath));
        Assert.False(File.Exists(ledger.DatabasePath));
    }

    [Fact]
    public async Task FirstOpen_CreatesNestedDirectoryAndFile()
    {
        using var ledger = new TempLedger();

        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.True(Directory.Exists(Path.GetDirectoryName(ledger.DatabasePath)));
        Assert.True(File.Exists(ledger.DatabasePath));
    }

    [Fact]
    public async Task FirstOpen_AppliesTheLatestSchemaVersion()
    {
        using var ledger = new TempLedger();

        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal((long)LedgerMigrations.LatestVersion, await ScalarAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'transactions';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'download_runs';"));
    }

    [Fact]
    public async Task TransactionsTable_HasExactlyTheVersionOneColumns()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('transactions') ORDER BY cid;";
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "id", "canonical_hash", "institution", "external_id", "amount",
                "date", "payee", "type", "plugin_version", "downloaded_at",
            ],
            columns);
    }

    [Fact]
    public async Task CanonicalHash_IsUnique()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);
        await InsertRowAsync(connection, "hash-1");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertRowAsync(connection, "hash-1"));

        Assert.Contains("UNIQUE", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstitutionIndex_Exists()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal(
            1L,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_transactions_institution';"));
    }

    [Fact]
    public async Task OpeningTwice_SharesDataAndKeepsVersion()
    {
        using var ledger = new TempLedger();
        await using var first = await ledger.Database.OpenAsync(Token);
        await InsertRowAsync(first, "hash-1");

        await using var second = await ledger.Database.OpenAsync(Token);

        Assert.Equal((long)LedgerMigrations.LatestVersion, await ScalarAsync(second, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(second, "SELECT COUNT(*) FROM transactions;"));
    }

    [Fact]
    public async Task NewInstanceOverExistingFile_WorksAndDataPersists()
    {
        using var ledger = new TempLedger();
        await using (var first = await ledger.Database.OpenAsync(Token))
        {
            await InsertRowAsync(first, "hash-1");
        }

        var reopened = new LedgerDatabase(ledger.DatabasePath, NullLogger<LedgerDatabase>.Instance);
        await using var second = await reopened.OpenAsync(Token);

        Assert.Equal(1L, await ScalarAsync(second, "SELECT COUNT(*) FROM transactions;"));
        Assert.Equal((long)LedgerMigrations.LatestVersion, await ScalarAsync(second, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task Connections_AreOpen_AndFileCanBeDeletedAfterDisposal()
    {
        using var ledger = new TempLedger();
        var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal(ConnectionState.Open, connection.State);

        await connection.DisposeAsync();

        File.Delete(ledger.DatabasePath);
        Assert.False(File.Exists(ledger.DatabasePath));
    }

    [Fact]
    public async Task PreCancelledToken_Throws_AndCreatesNothing()
    {
        using var ledger = new TempLedger();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ledger.Database.OpenAsync(cts.Token));

        Assert.False(Directory.Exists(ledger.DirectoryPath));
    }

    [Fact]
    public async Task ConcurrentOpens_AllSucceedAndLeaveTheLatestVersion()
    {
        using var ledger = new TempLedger();

        var connections = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ledger.Database.OpenAsync(Token)));
        try
        {
            Assert.Equal((long)LedgerMigrations.LatestVersion, await ScalarAsync(connections[0], "PRAGMA user_version;"));
        }
        finally
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(":memory:")]
    public void InternalConstructor_RejectsUnsupportedPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new LedgerDatabase(path, NullLogger<LedgerDatabase>.Instance));
    }

    [Fact]
    public void InternalConstructor_RejectsNullPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LedgerDatabase((string)null!, NullLogger<LedgerDatabase>.Instance));
    }

    [Fact]
    public async Task OptionsConstructor_UsesConfiguredAbsolutePath()
    {
        using var ledger = new TempLedger();
        var database = new LedgerDatabase(
            Options.Create(new FlushedYoOptions { LedgerPath = ledger.DatabasePath }),
            NullLogger<LedgerDatabase>.Instance);

        await using var connection = await database.OpenAsync(Token);

        Assert.True(File.Exists(ledger.DatabasePath));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(Token), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertRowAsync(SqliteConnection connection, string hash)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO transactions
                (canonical_hash, institution, external_id, amount, date, payee, type, plugin_version, downloaded_at)
            VALUES
                ($hash, 'fidelity', '1', '5.00', '2026-01-01', 'Test', 'debit', '1.0.0', '2026-01-01T00:00:00.0000000Z');
            """;
        command.Parameters.AddWithValue("$hash", hash);
        await command.ExecuteNonQueryAsync(Token);
    }
}
