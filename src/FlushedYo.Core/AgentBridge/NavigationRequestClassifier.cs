namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// The structured routing inputs for one intercepted request: the Playwright resource type, the request
/// URL exactly as the browser reports it, the origin pinned during a submission, and the freeze flag.
/// </summary>
internal readonly record struct NavigationRequest(
    string? ResourceType,
    string? Url,
    string? PinnedOrigin,
    bool IsFrozen)
{
    internal const string DocumentResourceType = "document";

    /// <summary>Gets a value indicating whether the request would replace the top-level document.</summary>
    internal bool IsDocument => string.Equals(ResourceType, DocumentResourceType, StringComparison.Ordinal);
}

/// <summary>The routing outcome for one request. Only the abort outcomes fault the session.</summary>
internal enum NavigationDisposition
{
    /// <summary>The request is not a document navigation, or it satisfies policy and the pin.</summary>
    Continue,

    /// <summary>Document navigation is frozen for the duration of a protected operation.</summary>
    AbortFrozen,

    /// <summary>The request URL is unparseable or outside the exact-origin allowlist.</summary>
    AbortDisallowed,

    /// <summary>A submission in flight may only reach the verified origin.</summary>
    AbortPinned,
}

/// <summary>The routing decision plus the value-free fault category recorded when it aborts.</summary>
internal readonly record struct NavigationRequestDecision(NavigationDisposition Disposition, string FaultCategory)
{
    private const string BlockedDocumentCategory = "blocked document request";

    /// <summary>Gets a value indicating whether the request must be aborted.</summary>
    internal bool IsAbort => Disposition != NavigationDisposition.Continue;

    internal static NavigationRequestDecision Continue() => new(NavigationDisposition.Continue, string.Empty);

    internal static NavigationRequestDecision Abort(NavigationDisposition disposition) =>
        new(disposition, BlockedDocumentCategory);
}

/// <summary>
/// Pure, total routing classifier. It reads only structured request metadata and the exact-origin policy;
/// no page text, no redirect history, and no wildcard matching participates in the decision.
/// </summary>
internal static class NavigationRequestClassifier
{
    internal static NavigationRequestDecision Classify(NavigationRequest request, ProviderBrowserPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!request.IsDocument)
        {
            return NavigationRequestDecision.Continue();
        }

        if (request.IsFrozen)
        {
            return NavigationRequestDecision.Abort(NavigationDisposition.AbortFrozen);
        }

        if (!TryResolveAllowedUrl(request.Url, policy, out var url))
        {
            return NavigationRequestDecision.Abort(NavigationDisposition.AbortDisallowed);
        }

        if (request.PinnedOrigin is not null &&
            !string.Equals(policy.GetOrigin(url), request.PinnedOrigin, StringComparison.Ordinal))
        {
            return NavigationRequestDecision.Abort(NavigationDisposition.AbortPinned);
        }

        return NavigationRequestDecision.Continue();
    }

    private static bool TryResolveAllowedUrl(string? url, ProviderBrowserPolicy policy, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || !policy.IsAllowed(parsed))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
