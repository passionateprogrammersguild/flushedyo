using FlushedYo.Core.Store;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>
/// An <see cref="IRunHistoryStore"/> with fixed contents. Its dictionaries are deliberately case-sensitive
/// so tests prove the status service does not rely on how a store keys its results.
/// </summary>
internal sealed class FakeRunHistory(
    IEnumerable<DownloadRun>? runs = null,
    IReadOnlyDictionary<string, long>? counts = null) : IRunHistoryStore
{
    private readonly Dictionary<string, DownloadRun> _runs = (runs ?? []).ToDictionary(static run => run.Plugin);
    private readonly IReadOnlyDictionary<string, long> _counts = counts ?? new Dictionary<string, long>();

    public Task RecordRunAsync(DownloadRun run, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("status must never record a run");

    public Task<IReadOnlyDictionary<string, DownloadRun>> GetLastRunsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, DownloadRun>>(_runs);

    public Task<IReadOnlyDictionary<string, long>> GetStoredTransactionCountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_counts);
}
