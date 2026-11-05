namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// The first value-free invariant that a cross-checked structured browser state violated. The
/// category never carries a URL, origin, or target identifier, so it is safe to surface.
/// </summary>
internal enum SnapshotViolation
{
    /// <summary>No invariant was violated.</summary>
    None,

    /// <summary>The target type, original identity, or single-page session invariants did not hold.</summary>
    TargetIdentity,

    /// <summary>The read frame was not the top-level frame with a committed loader.</summary>
    FrameIdentity,

    /// <summary>The structured document fields are absent, or the target, frame, and history URLs disagree.</summary>
    DocumentIdentity,

    /// <summary>The committed frame origin is unparseable, not allowed, or inconsistent with its frame URL.</summary>
    OriginIdentity,
}

/// <summary>The verified target, frame, document, and origin identity of one structured browser state.</summary>
internal readonly record struct SnapshotIdentity(
    string TargetId,
    string TopLevelFrameId,
    string LoaderId,
    string OriginKey);

/// <summary>The outcome of cross-checking one structured browser state.</summary>
internal readonly record struct SnapshotValidation(SnapshotViolation Violation, SnapshotIdentity Identity)
{
    internal static SnapshotValidation Failed(SnapshotViolation violation) =>
        new(violation, default);

    internal static SnapshotValidation Passed(BrowserSecurityState state, string originKey) =>
        new(SnapshotViolation.None, new SnapshotIdentity(state.TargetId!, state.TopLevelFrameId!, state.LoaderId!, originKey));
}

/// <summary>
/// Cross-checks structured browser state against one named invariant at a time. Every check is pure,
/// owns exactly one value-free violation category, and reads no page-derived prose.
/// </summary>
internal sealed class BrowserSnapshotValidator
{
    private const string PageTargetType = "page";
    private readonly ProviderBrowserPolicy _policy;
    private readonly string _originalTargetId;

    internal BrowserSnapshotValidator(ProviderBrowserPolicy policy, string originalTargetId)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(originalTargetId);
        _policy = policy;
        _originalTargetId = originalTargetId;
    }

    /// <summary>Returns the first violated invariant, or a validated identity and origin key.</summary>
    internal SnapshotValidation Validate(BrowserSecurityState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!HasTargetIdentity(state))
        {
            return SnapshotValidation.Failed(SnapshotViolation.TargetIdentity);
        }

        if (!HasTopLevelFrameIdentity(state))
        {
            return SnapshotValidation.Failed(SnapshotViolation.FrameIdentity);
        }

        if (!HasAgreeingDocumentUrls(state))
        {
            return SnapshotValidation.Failed(SnapshotViolation.DocumentIdentity);
        }

        return TryResolveAllowedOrigin(state, out var originKey)
            ? SnapshotValidation.Passed(state, originKey)
            : SnapshotValidation.Failed(SnapshotViolation.OriginIdentity);
    }

    /// <summary>Checks the target type, original target, and single-page session invariants.</summary>
    internal bool HasTargetIdentity(BrowserSecurityState state)
    {
        if (!IsPageTarget(state))
        {
            return false;
        }

        if (!IsOriginalTarget(state))
        {
            return false;
        }

        return HasSinglePageSession(state);
    }

    /// <summary>Checks that the read frame is the top-level frame and names a committed loader.</summary>
    internal static bool HasTopLevelFrameIdentity(BrowserSecurityState state)
    {
        if (!HasText(state.TopLevelFrameId))
        {
            return false;
        }

        if (!HasText(state.LoaderId))
        {
            return false;
        }

        return state.IsTopLevelFrame;
    }

    /// <summary>
    /// Checks that the target, frame, security-origin, and navigation-history fields are present and
    /// that the three URLs agree. A missing security origin belongs here, not to origin policy.
    /// </summary>
    internal static bool HasAgreeingDocumentUrls(BrowserSecurityState state)
    {
        if (!HasText(state.TargetUrl))
        {
            return false;
        }

        if (!HasText(state.FrameUrl))
        {
            return false;
        }

        if (!HasText(state.SecurityOrigin))
        {
            return false;
        }

        if (!HasText(state.NavigationHistoryUrl))
        {
            return false;
        }

        return UrlsAgree(state);
    }

    /// <summary>
    /// Resolves the committed origin key, requiring both the frame URL and the CDP security origin to
    /// parse, to be allowed by policy, and to name the same origin.
    /// </summary>
    internal bool TryResolveAllowedOrigin(BrowserSecurityState state, out string originKey)
    {
        originKey = string.Empty;
        if (!Uri.TryCreate(state.FrameUrl, UriKind.Absolute, out var documentUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(state.SecurityOrigin, UriKind.Absolute, out var securityOrigin))
        {
            return false;
        }

        if (!_policy.IsAllowed(documentUrl))
        {
            return false;
        }

        if (!_policy.IsAllowed(securityOrigin))
        {
            return false;
        }

        var documentOrigin = ProviderBrowserPolicy.OriginKey(documentUrl);
        if (!string.Equals(documentOrigin, ProviderBrowserPolicy.OriginKey(securityOrigin), StringComparison.Ordinal))
        {
            return false;
        }

        originKey = documentOrigin;
        return true;
    }

    private static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    private static bool IsPageTarget(BrowserSecurityState state)
    {
        if (!HasText(state.TargetId))
        {
            return false;
        }

        return string.Equals(state.TargetType, PageTargetType, StringComparison.Ordinal);
    }

    private bool IsOriginalTarget(BrowserSecurityState state) =>
        string.Equals(state.TargetId, _originalTargetId, StringComparison.Ordinal);

    private static bool HasSinglePageSession(BrowserSecurityState state)
    {
        if (state.PageTargetCount != 1)
        {
            return false;
        }

        if (state.TotalTargetCount != 1)
        {
            return false;
        }

        if (state.ContextPageCount != 1)
        {
            return false;
        }

        return IsLiveOriginalPage(state);
    }

    private static bool IsLiveOriginalPage(BrowserSecurityState state)
    {
        if (!state.IsOriginalPage)
        {
            return false;
        }

        if (!state.BrowserConnected)
        {
            return false;
        }

        return !state.PageClosed;
    }

    private static bool UrlsAgree(BrowserSecurityState state)
    {
        if (!string.Equals(state.TargetUrl, state.FrameUrl, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(state.NavigationHistoryUrl, state.FrameUrl, StringComparison.Ordinal);
    }
}
