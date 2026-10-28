using FlushedYo.Core.Store;
using static FlushedYo.Core.Tests.Store.RunTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Upgrading a v1 ledger and the v2 schema (spec: download_run_history_store).</summary>
public sealed class LedgerMigrationV2Tests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VersionOneDatabase_UpgradesInPlace_AndKeepsItsData()
    {
        using var ledger = new TempLedger();
        await using (var raw = ledger.OpenRawConnection())
        {
            await ExecuteAsync(raw, LedgerMigrations.All[0].Sql, Token);
            await ExecuteAsync(raw, "PRAGMA user_version = 1;", Token);
            await ExecuteAsync(
                raw,
                "INSERT INTO transactions (canonical_hash, institution, external_id, amount, date, payee, type, plugin_version, downloaded_at) " +
                "VALUES ('h', 'fidelity', '1', '5.00', '2026-01-01', 'Test', 'debit', '1.0.0', '2026-01-01T00:00:00.0000000Z');",
                Token);
        }

        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA user_version;", Token));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'download_runs';", Token));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM transactions;", Token));
    }

    [Fact]
    public async Task NewDatabase_EndsAtTheLatestVersion_WithBothTables()
    {
        using var ledger = new TempLedger();

        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal((long)LedgerMigrations.LatestVersion, await ScalarAsync(connection, "PRAGMA user_version;", Token));
        Assert.Equal(
            2L,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('transactions', 'download_runs');", Token));
    }

    [Fact]
    public async Task DownloadRuns_HasExactlyTheVersionTwoColumns()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('download_runs') ORDER BY cid;";
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(
            ["id", "plugin", "plugin_version", "started_at", "finished_at", "outcome", "inserted", "skipped", "rejected", "error"],
            columns);
    }

    [Fact]
    public async Task PluginIndex_Exists()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        Assert.Equal(
            1L,
            await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_download_runs_plugin';", Token));
    }

    [Fact]
    public async Task Exists_IsFalseBeforeFirstOpen_TrueAfter_AndNeverCreatesAnything()
    {
        using var ledger = new TempLedger();

        Assert.False(ledger.Database.Exists);
        Assert.False(Directory.Exists(ledger.DirectoryPath));
        Assert.False(File.Exists(ledger.DatabasePath));

        await using (await ledger.Database.OpenAsync(Token))
        {
        }

        Assert.True(ledger.Database.Exists);
    }
}
