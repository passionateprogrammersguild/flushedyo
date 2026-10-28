using FlushedYo.Core.Commands;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tests.Plugin;
using FlushedYo.Core.Tests.Store;
using FlushedYo.Core.Tests.Tracking;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>
/// A real tracker and ledger-backed stores over a temp ledger, a fixed clock, a capturing logger, and a
/// fake plugin loader, wired into a <see cref="DownloadService"/>.
/// </summary>
internal sealed class DownloadHarness : IDisposable
{
    public DownloadHarness()
    {
        Ledger = new TempLedger();
        Store = new SqliteTransactionStore(Ledger.Database);
        History = new SqliteRunHistoryStore(Ledger.Database);
        Tracker = new TransactionTracker(Store, Time, NullLogger<TransactionTracker>.Instance);
        Service = NewService();
    }

    public TempLedger Ledger { get; }

    public SqliteTransactionStore Store { get; }

    public SqliteRunHistoryStore History { get; }

    public FixedTimeProvider Time { get; } = new();

    public FakePluginLoader Loader { get; } = new();

    public CapturingLogger<DownloadService> Logger { get; } = new();

    public TransactionTracker Tracker { get; }

    public DownloadService Service { get; }

    public DownloadService NewService(IRunHistoryStore? history = null, TransactionTracker? tracker = null) =>
        new(Loader, tracker ?? Tracker, history ?? History, Time, Logger);

    public Task<long> CountRowsAsync(CancellationToken cancellationToken) =>
        TransactionTestData.CountRowsAsync(Ledger, cancellationToken);

    public Task<long> CountRunsAsync(CancellationToken cancellationToken) =>
        RunTestData.CountRunsAsync(Ledger, cancellationToken);

    public void Dispose() => Ledger.Dispose();
}
