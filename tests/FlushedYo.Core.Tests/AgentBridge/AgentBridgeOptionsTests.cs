using FlushedYo.Core.AgentBridge;
using FlushedYo.Core.Tests.Plugin;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class AgentBridgeOptionsTests
{
    private const string ExecutablePath = "/synthetic/path/to/custom-browser";

    [Fact]
    public void DefaultOptions_AreValidAndDenyEverything()
    {
        var options = new AgentBridgeOptions();

        options.Validate();

        Assert.Empty(options.AllowedOrigins);
        Assert.Null(options.BrowserExecutablePath);
        Assert.False(options.AllowBrowserExecutableOverride);
    }

    [Fact]
    public void CustomExecutableWithoutExplicitOptIn_IsRejectedWithoutEchoingThePath()
    {
        var options = new AgentBridgeOptions { BrowserExecutablePath = ExecutablePath };

        var exception = Assert.Throws<BridgeOperationException>(options.Validate);

        Assert.DoesNotContain(ExecutablePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomExecutableWithExplicitOptIn_IsAccepted()
    {
        var options = new AgentBridgeOptions
        {
            BrowserExecutablePath = ExecutablePath,
            AllowBrowserExecutableOverride = true,
        };

        options.Validate();
    }

    [Fact]
    public void BridgeConstructionRejectsAnUnapprovedExecutableOverride()
    {
        var options = new AgentBridgeOptions { BrowserExecutablePath = ExecutablePath };

        Assert.Throws<BridgeOperationException>(() => new TrustedBrowserBridge(
            new FakeBrowserSessionDriver(),
            options,
            new FakeSecretStore("synthetic"),
            new CapturingLogger<TrustedBrowserBridge>(),
            TimeProvider.System));
    }
}
