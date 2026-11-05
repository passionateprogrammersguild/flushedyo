using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class ProviderBrowserPolicyTests
{
    [Fact]
    public void EmptyAllowlist_DeniesEverything()
    {
        var policy = new ProviderBrowserPolicy([]);

        Assert.False(policy.IsAllowed(new Uri("https://example.com/login")));
        Assert.False(policy.IsAllowed(new Uri("http://127.0.0.1:8080/")));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com?query=1")]
    [InlineData("https://example.com#fragment")]
    [InlineData("https://user@example.com")]
    [InlineData("not-an-origin")]
    public void InvalidAllowlistEntries_ThrowWithoutEchoingEntry(string entry)
    {
        var exception = Assert.Throws<BridgePolicyException>(() => new ProviderBrowserPolicy([entry]));

        Assert.DoesNotContain(entry, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost")]
    [InlineData("https://127.0.0.1:8443")]
    public void LoopbackHttp_AndHttpsEntries_AreSupported(string entry)
    {
        var policy = new ProviderBrowserPolicy([entry]);

        Assert.True(policy.IsAllowed(new Uri(entry + "/login")));
    }

    [Fact]
    public void OriginMatching_IsExactForHostSchemeAndEffectivePort()
    {
        var policy = new ProviderBrowserPolicy(["https://example.com"]);

        Assert.True(policy.IsAllowed(new Uri("https://EXAMPLE.com:443/a?b=c")));
        Assert.False(policy.IsAllowed(new Uri("https://evil-example.com/")));
        Assert.False(policy.IsAllowed(new Uri("https://example.com.evil.com/")));
        Assert.False(policy.IsAllowed(new Uri("https://sub.example.com/")));
        Assert.False(policy.IsAllowed(new Uri("https://example.com:444/")));
        Assert.False(policy.IsAllowed(new Uri("http://example.com/")));
    }

    [Theory]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("about:blank")]
    [InlineData("data:text/html,https://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///tmp/example.com")]
    [InlineData("blob:https://example.com/id")]
    public void UserInfoAndOpaqueSchemes_AreDenied(string url)
    {
        var policy = new ProviderBrowserPolicy(["https://example.com"]);

        Assert.False(policy.IsAllowed(new Uri(url)));
    }

    [Fact]
    public void RelativeUrls_AreDenied()
    {
        var policy = new ProviderBrowserPolicy(["https://example.com"]);

        Assert.False(policy.IsAllowed(new Uri("/login", UriKind.Relative)));
    }

    [Fact]
    public void RedirectOutsideAllowlist_IsDeniedEvenWhenInitialOriginIsAllowed()
    {
        var policy = new ProviderBrowserPolicy(["https://example.com"]);
        var initial = new Uri("https://example.com/redirect");
        var redirect = new Uri("https://outside.example/landing");

        Assert.True(policy.IsAllowed(initial));
        Assert.False(policy.IsAllowed(redirect));
    }
}
