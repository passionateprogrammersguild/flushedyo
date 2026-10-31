using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

/// <summary>
/// Pins every structured spoof vector to exactly one value-free validator category, and pins the
/// reader-level exception type that each category produces.
/// </summary>
public sealed class BrowserSnapshotValidatorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(nameof(SnapshotViolation.None), "valid")]
    [InlineData(nameof(SnapshotViolation.None), "uppercase-host")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "target-null")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "target-empty")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "target-type-missing")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "target-type-iframe")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "target-replaced")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "page-targets-zero")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "page-targets-two")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "total-targets-two")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "context-pages-two")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "not-original-page")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "browser-disconnected")]
    [InlineData(nameof(SnapshotViolation.TargetIdentity), "page-closed")]
    [InlineData(nameof(SnapshotViolation.FrameIdentity), "frame-null")]
    [InlineData(nameof(SnapshotViolation.FrameIdentity), "frame-empty")]
    [InlineData(nameof(SnapshotViolation.FrameIdentity), "loader-null")]
    [InlineData(nameof(SnapshotViolation.FrameIdentity), "loader-empty")]
    [InlineData(nameof(SnapshotViolation.FrameIdentity), "subframe")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "target-url-null")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "target-url-empty")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "frame-url-null")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "history-null")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "history-empty")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "security-origin-null")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "security-origin-empty")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "target-url-differs")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "frame-url-differs")]
    [InlineData(nameof(SnapshotViolation.DocumentIdentity), "history-differs")]
    [InlineData(nameof(SnapshotViolation.OriginIdentity), "security-origin-relative")]
    [InlineData(nameof(SnapshotViolation.OriginIdentity), "security-origin-disallowed")]
    [InlineData(nameof(SnapshotViolation.OriginIdentity), "security-origin-different-allowed")]
    [InlineData(nameof(SnapshotViolation.OriginIdentity), "document-origin-disallowed")]
    [InlineData(nameof(SnapshotViolation.OriginIdentity), "unparseable-document-url")]
    public void Validate_ClassifiesEachSpoofVectorAsExactlyOneCategory(
        string expected,
        string vector)
    {
        var validation = Validator().Validate(BrowserStateVectors.State(vector));

        Assert.Equal(Enum.Parse<SnapshotViolation>(expected), validation.Violation);
    }

    [Fact]
    public void Validate_ValidState_ReturnsVerifiedIdentityAndOriginKey()
    {
        var validation = Validator().Validate(BrowserStateVectors.State("valid"));

        Assert.Equal(SnapshotViolation.None, validation.Violation);
        Assert.Equal("target-1", validation.Identity.TargetId);
        Assert.Equal("frame-1", validation.Identity.TopLevelFrameId);
        Assert.Equal("loader-1", validation.Identity.LoaderId);
        Assert.Equal("https://example.com:443", validation.Identity.OriginKey);
    }

    [Theory]
    [InlineData("target-null")]
    [InlineData("frame-null")]
    [InlineData("target-url-differs")]
    [InlineData("security-origin-null")]
    public async Task Reader_IdentityViolations_KeepSnapshotException(string vector)
    {
        var reader = ReaderFor(vector);

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => reader.ReadAsync(Token));
    }

    [Theory]
    [InlineData("security-origin-disallowed")]
    [InlineData("security-origin-different-allowed")]
    [InlineData("unparseable-document-url")]
    public async Task Reader_OriginViolations_KeepPolicyException(string vector)
    {
        var reader = ReaderFor(vector);

        await Assert.ThrowsAsync<BridgePolicyException>(() => reader.ReadAsync(Token));
    }

    private static BrowserSecuritySnapshotReader ReaderFor(string vector) =>
        new(new StaticSecurityStateSource(BrowserStateVectors.State(vector)), Policy(), "target-1");

    private static BrowserSnapshotValidator Validator() => new(Policy(), "target-1");

    private static ProviderBrowserPolicy Policy() => new(["https://example.com", "https://other.example"]);

    private sealed class StaticSecurityStateSource(BrowserSecurityState state) : IBrowserSecurityStateSource
    {
        public long SecurityVersion => 1;

        public Task<BrowserSecurityState> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(state);
        }
    }
}
