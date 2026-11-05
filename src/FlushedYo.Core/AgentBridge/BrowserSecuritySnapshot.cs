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

    /// <summary>
    /// Gets a value indicating whether this snapshot names the same verified document as
    /// <paramref name="other"/>: the same target, top-level frame, committed loader, and origin.
    /// <see cref="SecurityVersion"/> is deliberately excluded — it advances on any benign browser
    /// event (an intercepted request, a frame update), so it proves one read was stable; it is not
    /// part of the identity a protected operation must still match.
    /// </summary>
    internal bool HasSameDocumentIdentity(BrowserSecuritySnapshot? other) =>
        other is not null &&
        string.Equals(TargetId, other.TargetId, StringComparison.Ordinal) &&
        string.Equals(TopLevelFrameId, other.TopLevelFrameId, StringComparison.Ordinal) &&
        string.Equals(LoaderId, other.LoaderId, StringComparison.Ordinal) &&
        string.Equals(Origin, other.Origin, StringComparison.Ordinal);
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
    private readonly BrowserSnapshotValidator _validator;

    internal BrowserSecuritySnapshotReader(
        IBrowserSecurityStateSource source,
        ProviderBrowserPolicy policy,
        string originalTargetId)
    {
        _source = source;
        _validator = new BrowserSnapshotValidator(policy, originalTargetId);
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
        var validation = _validator.Validate(state);
        if (validation.Violation != SnapshotViolation.None)
        {
            throw FailureFor(validation.Violation);
        }

        return new BrowserSecuritySnapshot(
            validation.Identity.TargetId,
            validation.Identity.TopLevelFrameId,
            validation.Identity.LoaderId,
            validation.Identity.OriginKey,
            securityVersion);
    }

    private static BridgeException FailureFor(SnapshotViolation violation) =>
        violation == SnapshotViolation.OriginIdentity
            ? new BridgePolicyException("The committed browser origin is missing, inconsistent, or not allowed.")
            : new BridgeSnapshotException("The browser target, frame, or navigation identity is ambiguous.");
}
