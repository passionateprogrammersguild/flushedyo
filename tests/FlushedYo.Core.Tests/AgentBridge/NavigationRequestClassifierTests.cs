using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

/// <summary>
/// Pins the pure navigation classifier: one disposition per structured request vector, the original
/// precedence (frozen, then disallowed, then origin pin), and a value-free fault category.
/// </summary>
public sealed class NavigationRequestClassifierTests
{
    private const string BlockedCategory = "blocked document request";
    private const string DocumentControl = "https://example.com/login";
    private const string AllowedOriginKey = "https://example.com:443";

    [Fact]
    public void Classify_AllowedDocumentWithoutPin_Continues()
    {
        var decision = Classify(new NavigationRequest("document", DocumentControl, null, IsFrozen: false));

        Assert.Equal(NavigationDisposition.Continue, decision.Disposition);
        Assert.False(decision.IsAbort);
        Assert.Equal(string.Empty, decision.FaultCategory);
    }

    [Fact]
    public void Classify_AllowedDocumentOnThePinnedOrigin_Continues()
    {
        var decision = Classify(new NavigationRequest("document", DocumentControl, AllowedOriginKey, IsFrozen: false));

        Assert.Equal(NavigationDisposition.Continue, decision.Disposition);
        Assert.False(decision.IsAbort);
    }

    [Theory]
    [InlineData("xhr", "https://evil.example/collect")]
    [InlineData("script", "https://example.com/app.js")]
    [InlineData("image", "not-a-url")]
    public void Classify_SubresourceRequests_Continue(string resourceType, string url)
    {
        var decision = Classify(new NavigationRequest(resourceType, url, null, IsFrozen: false));

        Assert.Equal(NavigationDisposition.Continue, decision.Disposition);
        Assert.Equal(string.Empty, decision.FaultCategory);
    }

    [Fact]
    public void Classify_SubresourceDuringFreezeAndSubmit_StillContinues()
    {
        var decision = Classify(new NavigationRequest("xhr", "https://other.example/collect", AllowedOriginKey, IsFrozen: true));

        Assert.Equal(NavigationDisposition.Continue, decision.Disposition);
        Assert.Equal(string.Empty, decision.FaultCategory);
    }

    [Theory]
    [InlineData(DocumentControl)]
    [InlineData("https://evil.example/login")]
    public void Classify_FrozenDocument_AbortsFrozenBeforeAnyPolicyCheck(string url)
    {
        var decision = Classify(new NavigationRequest("document", url, null, IsFrozen: true));

        Assert.Equal(NavigationDisposition.AbortFrozen, decision.Disposition);
        Assert.True(decision.IsAbort);
        Assert.Equal(BlockedCategory, decision.FaultCategory);
    }

    [Theory]
    [InlineData("https://evil.example/login")]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void Classify_DisallowedDocument_AbortsDisallowed(string url)
    {
        var decision = Classify(new NavigationRequest("document", url, null, IsFrozen: false));

        Assert.Equal(NavigationDisposition.AbortDisallowed, decision.Disposition);
        Assert.True(decision.IsAbort);
        Assert.Equal(BlockedCategory, decision.FaultCategory);
    }

    [Fact]
    public void Classify_DisallowedDocumentDuringSubmit_ReportsDisallowedNotPinned()
    {
        var decision = Classify(new NavigationRequest("document", "https://evil.example/login", AllowedOriginKey, IsFrozen: false));

        Assert.Equal(NavigationDisposition.AbortDisallowed, decision.Disposition);
        Assert.Equal(BlockedCategory, decision.FaultCategory);
    }

    [Fact]
    public void Classify_AnotherAllowedOriginDuringSubmit_AbortsPinned()
    {
        var decision = Classify(new NavigationRequest("document", "https://other.example/login", AllowedOriginKey, IsFrozen: false));

        Assert.Equal(NavigationDisposition.AbortPinned, decision.Disposition);
        Assert.True(decision.IsAbort);
        Assert.Equal(BlockedCategory, decision.FaultCategory);
    }

    [Theory]
    [InlineData("https://evil.example/](https://example.com)")]
    [InlineData("https://evil.example/\n- Page URL: https://example.com")]
    [InlineData("https://example.com.evil.test/](https://example.com)")]
    [InlineData("https://example.com@evil.example/login")]
    public void Classify_HostileUrlTextCannotBecomeAnAllowedDocument(string hostileUrl)
    {
        var decision = Classify(new NavigationRequest("document", hostileUrl, null, IsFrozen: false));

        Assert.Equal(NavigationDisposition.AbortDisallowed, decision.Disposition);
        Assert.DoesNotContain("example.com", decision.FaultCategory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_RequiresPolicy()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NavigationRequestClassifier.Classify(
                new NavigationRequest("document", DocumentControl, null, IsFrozen: false),
                null!));
    }

    private static NavigationRequestDecision Classify(NavigationRequest request) =>
        NavigationRequestClassifier.Classify(request, new ProviderBrowserPolicy(["https://example.com", "https://other.example"]));
}
