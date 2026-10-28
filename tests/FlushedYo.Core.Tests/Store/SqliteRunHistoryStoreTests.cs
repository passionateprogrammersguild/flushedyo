using FlushedYo.Core.Models;
using FlushedYo.Core.Store;
using Microsoft.Extensions.Logging.Abstractions;
using static FlushedYo.Core.Tests.Store.RunTestData;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Record and read behavior (spec: download_run_history_store).</summary>
public sealed class SqliteRunHistoryStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SqliteRunHistoryStore CreateStore(TempLedger ledger) => new(ledger.Database);

    [Fact]
    public async Task RecordedRun_RoundTripsEveryField()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var run = Run(plugin: "fidelity", pluginVersion: "2.1.0", inserted: 3, skipped: 2, rejected: 1);

        await store.RecordRunAsync(run, Token);
        var last = await store.GetLastRunsAsync(Token);

        Assert.Equal(run, last["fidelity"]);
        Assert.Null(last["fidelity"].Error);
    }

    [Fact]
    public async Task FailedRun_RoundTripsItsError_WithZeroCounts()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var run = Run(outcome: DownloadOutcome.Failed, inserted: 0, skipped: 0, rejected: 0, error: "Plugin threw InvalidOperationException.");

        await store.RecordRunAsync(run, Token);

        Assert.Equal(run, (await store.GetLastRunsAsync(Token))["stub"]);
    }

    [Fact]
    public async Task OnlyTheLatestRunPerPlugin_IsReturned_ByRecordOrderNotByTime()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        await store.RecordRunAsync(Run(startedAt: DefaultStart.AddDays(10), inserted: 1), Token);
        await store.RecordRunAsync(Run(startedAt: DefaultStart.AddDays(20), inserted: 2), Token);
        var latest = Run(startedAt: DefaultStart, inserted: 3);
        await store.RecordRunAsync(latest, Token);

        var last = await store.GetLastRunsAsync(Token);

        Assert.Equal(latest, Assert.Single(last).Value);
    }

    [Fact]
    public async Task EachPlugin_GetsItsOwnLatestRun_AndLookupIsCaseInsensitive()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var alpha = Run(plugin: "alpha", inserted: 1);
        var bravo = Run(plugin: "bravo", inserted: 2);
        await store.RecordRunAsync(alpha, Token);
        await store.RecordRunAsync(bravo, Token);

        var last = await store.GetLastRunsAsync(Token);

        Assert.Equal(2, last.Count);
        Assert.Equal(alpha, last["ALPHA"]);
        Assert.Equal(bravo, last["Bravo"]);
    }

    [Fact]
    public async Task LongError_IsStoredAsExactlyFiveHundredCharacters()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);

        await store.RecordRunAsync(Run(outcome: DownloadOutcome.Failed, error: new string('x', 600)), Token);

        Assert.Equal(new string('x', 500), (await store.GetLastRunsAsync(Token))["stub"].Error);
    }

    [Fact]
    public async Task NonUtcTimes_ComeBackAsTheSameInstants()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var start = new DateTimeOffset(2026, 3, 4, 10, 6, 7, TimeSpan.FromHours(5));
        await store.RecordRunAsync(Run(startedAt: start, finishedAt: start.AddSeconds(9)), Token);

        var run = (await store.GetLastRunsAsync(Token))["stub"];

        Assert.Equal(start.UtcDateTime, run.StartedAt.UtcDateTime);
        Assert.Equal(start.AddSeconds(9).UtcDateTime, run.FinishedAt.UtcDateTime);
    }

    [Fact]
    public async Task StoredTransactionCounts_GroupByLowerCasedInstitution()
    {
        using var ledger = new TempLedger();
        var store = new SqliteTransactionStore(ledger.Database);
        Transaction[] rows =
        [
            Tx(institution: "Fidelity", externalId: "1"),
            Tx(institution: "Fidelity", externalId: "2"),
            Tx(institution: "schwab", externalId: "3"),
        ];
        foreach (var row in rows)
        {
            await store.RecordTransactionAsync(row, Audit(row), Token);
        }

        var counts = await CreateStore(ledger).GetStoredTransactionCountsAsync(Token);

        Assert.Equal(2, counts.Count);
        Assert.Equal(2L, counts["fidelity"]);
        Assert.Equal(2L, counts["FIDELITY"]);
        Assert.Equal(1L, counts["schwab"]);
    }

    [Fact]
    public async Task Runs_SurviveANewStoreAndLedgerInstance()
    {
        using var ledger = new TempLedger();
        var run = Run();
        await CreateStore(ledger).RecordRunAsync(run, Token);

        var reopened = new SqliteRunHistoryStore(new LedgerDatabase(ledger.DatabasePath, NullLogger<LedgerDatabase>.Instance));

        Assert.Equal(run, (await reopened.GetLastRunsAsync(Token))["stub"]);
    }

    [Fact]
    public async Task Reads_OnAnUnopenedLedger_ReturnEmptyAndCreateNothing()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);

        Assert.Empty(await store.GetLastRunsAsync(Token));
        Assert.Empty(await store.GetStoredTransactionCountsAsync(Token));

        Assert.False(Directory.Exists(ledger.DirectoryPath));
        Assert.False(File.Exists(ledger.DatabasePath));
    }

    [Fact]
    public async Task RecordingARun_CreatesTheLedger()
    {
        using var ledger = new TempLedger();

        await CreateStore(ledger).RecordRunAsync(Run(), Token);

        Assert.True(File.Exists(ledger.DatabasePath));
    }
}
