namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Immutable browser-authoritative identity for one top-level document. Values originate only in
/// structured CDP responses; no title, DOM text, accessibility text, or other page prose is included.
/// </summary>
public sealed record BrowserSecuritySnapshot
{
    internal BrowserSecuritySnapshot(
        string targetId,
        string topLevelFrameId,
        string loaderId,
        string origin,
        long securityVersion)
    {
        TargetId = targetId;
        TopLevelFrameId = topLevelFrameId;
        LoaderId = loaderId;
        Origin = origin;
        SecurityVersion = securityVersion;
    }

    /// <summary>Gets the CDP target identifier.</summary>
    public string TargetId { get; }

    /// <summary>Gets the CDP top-level frame identifier.</summary>
    public string TopLevelFrameId { get; }

    /// <summary>Gets the CDP loader identifier for the committed document.</summary>
    public string LoaderId { get; }

    /// <summary>Gets the normalized scheme, host, and effective port, without a path or query.</summary>
    public string Origin { get; }

    /// <summary>Gets the monotonic browser security-state version.</summary>
    public long SecurityVersion { get; }
}

internal sealed record BrowserSecurityState
{
    internal string? TargetId { get; init; }

    internal string? TargetType { get; init; }

    internal string? TargetUrl { get; init; }

    internal string? TopLevelFrameId { get; init; }

    internal string? LoaderId { get; init; }

    internal string? FrameUrl { get; init; }

    internal string? SecurityOrigin { get; init; }

    internal bool IsTopLevelFrame { get; init; }

    internal string? NavigationHistoryUrl { get; init; }

    internal int PageTargetCount { get; init; }

    internal int TotalTargetCount { get; init; }

    internal int ContextPageCount { get; init; }

    internal bool IsOriginalPage { get; init; }

    internal bool BrowserConnected { get; init; }

    internal bool PageClosed { get; init; }
}

internal interface IBrowserSecurityStateSource
{
    long SecurityVersion { get; }

    Task<BrowserSecurityState> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads and cross-checks only structured browser state, with bounded stability retries.</summary>
internal sealed class BrowserSecuritySnapshotReader
{
    private const int MaximumAttempts = 4;
    private readonly IBrowserSecurityStateSource _source;
    private readonly ProviderBrowserPolicy _policy;
    private readonly string _originalTargetId;

    internal BrowserSecuritySnapshotReader(
        IBrowserSecurityStateSource source,
        ProviderBrowserPolicy policy,
        string originalTargetId)
    {
        _source = source;
        _policy = policy;
        _originalTargetId = originalTargetId;
    }

    internal async Task<BrowserSecuritySnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versionBefore = _source.SecurityVersion;
            BrowserSecurityState state;
            try
            {
                state = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (BridgeException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new BridgeSnapshotException("The browser security snapshot could not be read.");
            }

            var versionAfter = _source.SecurityVersion;
            if (versionBefore != versionAfter)
            {
                continue;
            }

            return CrossCheck(state, versionAfter);
        }

        throw new BridgeSnapshotException("The browser security snapshot remained unstable.");
    }

    private BrowserSecuritySnapshot CrossCheck(BrowserSecurityState state, long securityVersion)
    {
        if (string.IsNullOrEmpty(state.TargetId) ||
            state.TargetType != "page" ||
            string.IsNullOrEmpty(state.TargetUrl) ||
            string.IsNullOrEmpty(state.TopLevelFrameId) ||
            string.IsNullOrEmpty(state.LoaderId) ||
            string.IsNullOrEmpty(state.FrameUrl) ||
            string.IsNullOrEmpty(state.SecurityOrigin) ||
            string.IsNullOrEmpty(state.NavigationHistoryUrl) ||
            !state.IsTopLevelFrame ||
            state.PageTargetCount != 1 ||
            state.TotalTargetCount != 1 ||
            state.ContextPageCount != 1 ||
            !state.IsOriginalPage ||
            !state.BrowserConnected ||
            state.PageClosed ||
            !string.Equals(state.TargetId, _originalTargetId, StringComparison.Ordinal) ||
            !string.Equals(state.TargetUrl, state.FrameUrl, StringComparison.Ordinal) ||
            !string.Equals(state.NavigationHistoryUrl, state.FrameUrl, StringComparison.Ordinal))
        {
            throw new BridgeSnapshotException("The browser target, frame, or navigation identity is ambiguous.");
        }

        if (!Uri.TryCreate(state.FrameUrl, UriKind.Absolute, out var documentUrl) ||
            !Uri.TryCreate(state.SecurityOrigin, UriKind.Absolute, out var securityOrigin) ||
            !_policy.IsAllowed(documentUrl) ||
            !_policy.IsAllowed(securityOrigin) ||
            !string.Equals(
                ProviderBrowserPolicy.OriginKey(documentUrl),
                ProviderBrowserPolicy.OriginKey(securityOrigin),
                StringComparison.Ordinal))
        {
            throw new BridgePolicyException("The committed browser origin is missing, inconsistent, or not allowed.");
        }

        return new BrowserSecuritySnapshot(
            state.TargetId,
            state.TopLevelFrameId,
            state.LoaderId,
            ProviderBrowserPolicy.OriginKey(documentUrl),
            securityVersion);
    }
}
