using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

/// <summary>
/// Named structured-state spoof vectors. Each name owns exactly one mutation of an otherwise valid
/// state, so a theory row cannot accidentally combine two violations.
/// </summary>
internal static class BrowserStateVectors
{
    private static readonly Dictionary<string, Func<BrowserSecurityState, BrowserSecurityState>> Mutations =
        new(StringComparer.Ordinal)
        {
            ["valid"] = state => state,
            ["uppercase-host"] = state => state with
            {
                TargetUrl = "https://EXAMPLE.com/login",
                FrameUrl = "https://EXAMPLE.com/login",
                NavigationHistoryUrl = "https://EXAMPLE.com/login",
                SecurityOrigin = "https://EXAMPLE.com",
            },
            ["target-null"] = state => state with { TargetId = null },
            ["target-empty"] = state => state with { TargetId = string.Empty },
            ["target-type-missing"] = state => state with { TargetType = null },
            ["target-type-iframe"] = state => state with { TargetType = "iframe" },
            ["target-replaced"] = state => state with { TargetId = "target-2" },
            ["page-targets-zero"] = state => state with { PageTargetCount = 0 },
            ["page-targets-two"] = state => state with { PageTargetCount = 2 },
            ["total-targets-two"] = state => state with { TotalTargetCount = 2 },
            ["context-pages-two"] = state => state with { ContextPageCount = 2 },
            ["not-original-page"] = state => state with { IsOriginalPage = false },
            ["browser-disconnected"] = state => state with { BrowserConnected = false },
            ["page-closed"] = state => state with { PageClosed = true },
            ["frame-null"] = state => state with { TopLevelFrameId = null },
            ["frame-empty"] = state => state with { TopLevelFrameId = string.Empty },
            ["loader-null"] = state => state with { LoaderId = null },
            ["loader-empty"] = state => state with { LoaderId = string.Empty },
            ["subframe"] = state => state with { IsTopLevelFrame = false },
            ["target-url-null"] = state => state with { TargetUrl = null },
            ["target-url-empty"] = state => state with { TargetUrl = string.Empty },
            ["frame-url-null"] = state => state with { FrameUrl = null },
            ["history-null"] = state => state with { NavigationHistoryUrl = null },
            ["history-empty"] = state => state with { NavigationHistoryUrl = string.Empty },
            ["security-origin-null"] = state => state with { SecurityOrigin = null },
            ["security-origin-empty"] = state => state with { SecurityOrigin = string.Empty },
            ["target-url-differs"] = state => state with { TargetUrl = "https://example.com/other" },
            ["frame-url-differs"] = state => state with { FrameUrl = "https://example.com/other" },
            ["history-differs"] = state => state with { NavigationHistoryUrl = "https://example.com/other" },
            ["security-origin-relative"] = state => state with { SecurityOrigin = "example.com" },
            ["security-origin-disallowed"] = state => state with { SecurityOrigin = "https://evil.example" },
            ["security-origin-different-allowed"] = state => state with { SecurityOrigin = "https://other.example" },
            ["document-origin-disallowed"] = state => state with
            {
                TargetUrl = "https://evil.example/login",
                FrameUrl = "https://evil.example/login",
                NavigationHistoryUrl = "https://evil.example/login",
                SecurityOrigin = "https://evil.example",
            },
            ["unparseable-document-url"] = state => state with
            {
                TargetUrl = "not-a-url",
                FrameUrl = "not-a-url",
                NavigationHistoryUrl = "not-a-url",
                SecurityOrigin = "not-a-url",
            },
        };

    /// <summary>Returns the mutated state for one named vector.</summary>
    internal static BrowserSecurityState State(string vector) =>
        Mutations.TryGetValue(vector, out var mutation)
            ? mutation(Valid())
            : throw new ArgumentOutOfRangeException(nameof(vector));

    /// <summary>Returns a state that satisfies every structured invariant with one page and one document.</summary>
    internal static BrowserSecurityState Valid() => new()
    {
        TargetId = "target-1",
        TargetType = "page",
        TargetUrl = "https://example.com/login",
        TopLevelFrameId = "frame-1",
        LoaderId = "loader-1",
        FrameUrl = "https://example.com/login",
        SecurityOrigin = "https://example.com",
        IsTopLevelFrame = true,
        NavigationHistoryUrl = "https://example.com/login",
        PageTargetCount = 1,
        TotalTargetCount = 1,
        ContextPageCount = 1,
        IsOriginalPage = true,
        BrowserConnected = true,
        PageClosed = false,
    };
}
