namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// The structured facts of one browser download, read only from Chromium's CDP download events.
/// It carries the browser-assigned identifier, the suggested file name, and the url that served the
/// artifact — never file content. The identifier is the only name the artifact has on disk.
/// </summary>
internal readonly record struct DownloadBegin(string Guid, string? SuggestedFileName, string? Url);

/// <summary>
/// Outcome of one completed download claim: the begin facts the artifact was claimed under.
/// </summary>
internal readonly record struct DownloadSignal(DownloadBegin Begin);

/// <summary>
/// Rendezvous for exactly one download inside an open download window. Every member is safe to call
/// from a CDP event thread and from the claiming operation; a second download is refused rather than
/// queued, so the window can never accumulate artifacts.
/// </summary>
internal sealed class DownloadCapture
{
    private const string CompletedState = "completed";
    private const string CanceledState = "canceled";
    private const string DuplicateReason = "a second download";
    private const string MismatchedReason = "a mismatched download";

    private readonly TaskCompletionSource<DownloadBegin> _begin =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _terminal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Records the first announced download and refuses every later announcement.</summary>
    internal void Announce(DownloadBegin begin)
    {
        if (!_begin.TrySetResult(begin))
        {
            _terminal.TrySetResult(DuplicateReason);
        }
    }

    /// <summary>
    /// Records a progress report for the announced download. Only the browser's terminal states settle
    /// the capture; the initial <c>inProgress</c> report is a liveness signal, not a failure.
    /// </summary>
    internal void Progress(string? guid, string? state)
    {
        if (!IsTerminal(state))
        {
            return;
        }

        if (IsMismatchedAnnouncement(guid))
        {
            _terminal.TrySetResult(MismatchedReason);
            return;
        }

        _terminal.TrySetResult(string.Equals(state, CompletedState, StringComparison.Ordinal)
            ? CompletedState
            : CanceledState);
    }

    private bool IsMismatchedAnnouncement(string? guid) =>
        guid is not null &&
        _begin.Task.IsCompletedSuccessfully &&
        !string.Equals(_begin.Task.Result.Guid, guid, StringComparison.Ordinal);

    /// <summary>
    /// Waits for the announcement and its terminal state. The wait is bounded by the caller's timeout,
    /// and an interrupted download surfaces as a value-free reason instead of a partial artifact.
    /// </summary>
    internal async Task<DownloadSignal> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var begin = await _begin.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        var terminal = await _terminal.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(terminal, CompletedState, StringComparison.Ordinal))
        {
            throw new BridgeOperationException($"The download did not complete ({terminal}).");
        }

        return new DownloadSignal(begin);
    }

    private static bool IsTerminal(string? state) =>
        string.Equals(state, CompletedState, StringComparison.Ordinal) ||
        string.Equals(state, CanceledState, StringComparison.Ordinal);
}
