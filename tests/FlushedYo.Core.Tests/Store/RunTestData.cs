using System.Globalization;
using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Shared builders and raw SQL helpers for run-history tests.</summary>
internal static class RunTestData
{
    public static readonly DateTimeOffset DefaultStart = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    public static DownloadRun Run(
        string plugin = "stub",
        string pluginVersion = "1.0.0",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? finishedAt = null,
        DownloadOutcome outcome = DownloadOutcome.Succeeded,
        int inserted = 3,
        int skipped = 1,
        int rejected = 0,
        string? error = null)
    {
        var start = startedAt ?? DefaultStart;
        return new DownloadRun(plugin, pluginVersion, start, finishedAt ?? start.AddSeconds(5), outcome, inserted, skipped, rejected, error);
    }

    public static async Task<long> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<long> CountRunsAsync(TempLedger ledger, CancellationToken cancellationToken)
    {
        await using var connection = await ledger.Database.OpenAsync(cancellationToken);
        return await ScalarAsync(connection, "SELECT COUNT(*) FROM download_runs;", cancellationToken);
    }
}
