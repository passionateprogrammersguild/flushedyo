using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;
using static FlushedYo.Core.Tests.Store.RunTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Validation, SQL safety, and cancellation (spec: download_run_history_store).</summary>
public sealed class SqliteRunHistoryStoreValidationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, DownloadRun> InvalidRuns => new()
    {
        { "blank plugin", Run(plugin: " ") },
        { "empty plugin", Run(plugin: "") },
        { "blank version", Run(pluginVersion: "  ") },
        { "negative inserted", Run(inserted: -1) },
        { "negative skipped", Run(skipped: -1) },
        { "negative rejected", Run(rejected: -1) },
        { "finished before started", Run(startedAt: DefaultStart, finishedAt: DefaultStart.AddSeconds(-1)) },
        { "failed without error", Run(outcome: DownloadOutcome.Failed, error: null) },
        { "failed with blank error", Run(outcome: DownloadOutcome.Failed, error: "  ") },
        { "succeeded with error", Run(outcome: DownloadOutcome.Succeeded, error: "unexpected") },
        { "undefined outcome", Run(outcome: (DownloadOutcome)99) },
    };

    [Theory]
    [MemberData(nameof(InvalidRuns))]
    public async Task InvalidRun_Throws_AndStoresNothing(string description, DownloadRun run)
    {
        _ = description;
        using var ledger = new TempLedger();

        await Assert.ThrowsAsync<ArgumentException>(() => new SqliteRunHistoryStore(ledger.Database).RecordRunAsync(run, Token));

        Assert.Equal(0, await CountRunsAsync(ledger, Token));
    }

    [Fact]
    public async Task NullRun_Throws()
    {
        using var ledger = new TempLedger();

        await Assert.ThrowsAsync<ArgumentNullException>(() => new SqliteRunHistoryStore(ledger.Database).RecordRunAsync(null!, Token));
    }

    [Fact]
    public async Task SqlInjectionAttempt_IsStoredLiterally_AndTheTableSurvives()
    {
        using var ledger = new TempLedger();
        var store = new SqliteRunHistoryStore(ledger.Database);
        var run = Run(plugin: "x'); DROP TABLE download_runs;--");

        await store.RecordRunAsync(run, Token);

        Assert.Equal(run, (await store.GetLastRunsAsync(Token))["x'); DROP TABLE download_runs;--"]);
        Assert.Equal(1, await CountRunsAsync(ledger, Token));
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsForEveryOperation()
    {
        using var ledger = new TempLedger();
        var store = new SqliteRunHistoryStore(ledger.Database);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RecordRunAsync(Run(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetLastRunsAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetStoredTransactionCountsAsync(cts.Token));
    }

    [Fact]
    public async Task OutcomeCheckConstraint_RejectsUnknownLiterals()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO download_runs (plugin, plugin_version, started_at, finished_at, outcome, inserted, skipped, rejected) " +
            "VALUES ('p', '1.0.0', 's', 'f', 'bogus', 0, 0, 0);",
            Token));
    }

    [Fact]
    public async Task CountCheckConstraints_RejectNegativeValues()
    {
        using var ledger = new TempLedger();
        await using var connection = await ledger.Database.OpenAsync(Token);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO download_runs (plugin, plugin_version, started_at, finished_at, outcome, inserted, skipped, rejected) " +
            "VALUES ('p', '1.0.0', 's', 'f', 'succeeded', -1, 0, 0);",
            Token));
    }
}
