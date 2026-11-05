using FlushedYo.Core.AgentBridge;
using System.Globalization;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class TrustedBrowserBridgeDownloadTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Download_ReturnsArtifactAndVerifiesSnapshotBeforeClickAndAfterClaim()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.DownloadHandler = (request, verify, token) =>
            LandingAsync(request, verify, token);

        var artifact = await harness.Bridge.DownloadStatementAsync(
            "#export",
            expected,
            DownloadStatementOptions.Create(harness.Directory, 4096, TimeSpan.FromSeconds(1)),
            Token);

        AssertDownloadSucceeded(harness, artifact, expected);
    }

    private static async Task<BrowserArtifact> LandingAsync(
        DownloadRequest request,
        Func<CancellationToken, Task> verify,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(request.Options.DestinationDirectory, "transactions.csv");
        await File.WriteAllTextAsync(path, "synthetic", cancellationToken);
        await verify(cancellationToken);
        return new BrowserArtifact(path, new FileInfo(path).Length, request.ExpectedSnapshot.Origin);
    }

    private static void AssertDownloadSucceeded(
        BridgeTestHarness harness,
        BrowserArtifact artifact,
        BrowserSecuritySnapshot expected)
    {
        Assert.Equal(expected.Origin, artifact.Origin);
        Assert.Equal("transactions.csv", Path.GetFileName(artifact.Path));
        Assert.True(artifact.SizeBytes > 0);
        Assert.Equal(1, harness.Driver.DownloadCallCount);
        // The bridge re-verified before the seam and again after the claim.
        Assert.True(harness.Driver.VerificationCount >= 2);
        Assert.False(harness.Driver.Closed);
        Assert.Empty(harness.Logger.Entries);
    }

    [Theory]
    [InlineData(PreSeamRejection.DisallowedOrigin,
        "https://outside.example:443", "00:00:01", typeof(BridgePolicyException))]
    [InlineData(PreSeamRejection.TimeoutBeyondBound,
        null, "00:01:00", typeof(ArgumentOutOfRangeException))]
    public async Task PreSeamRejection_FailsBeforeTheSeamWithoutClosingNothingWasOpened(
        PreSeamRejection rejection,
        string? origin,
        string timeoutText,
        Type exceptionType)
    {
        using var harness = new BridgeTestHarness();
        var expected = rejection switch
        {
            PreSeamRejection.DisallowedOrigin => TrustedBrowserBridgeCredentialTests.Snapshot(origin: origin!),
            PreSeamRejection.TimeoutBeyondBound => harness.Driver.CurrentSnapshot,
            _ => throw new ArgumentOutOfRangeException(nameof(rejection)),
        };

        var exception = await Record.ExceptionAsync(() =>
            harness.Bridge.DownloadStatementAsync(
                "#export",
                expected,
                DownloadStatementOptions.Create(
                    harness.Directory, 4096, TimeSpan.Parse(timeoutText, CultureInfo.InvariantCulture)),
                Token));

        Assert.IsType(exceptionType, exception);
        Assert.Equal(0, harness.Driver.DownloadCallCount);
    }

    public enum PreSeamRejection
    {
        DisallowedOrigin,
        TimeoutBeyondBound,
    }

    [Fact]
    public async Task ArtifactFromADifferentOrigin_FailsClosed()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.DownloadHandler = (request, _, token) =>
        {
            var path = Path.Combine(request.Options.DestinationDirectory, "transactions.csv");
            File.WriteAllText(path, "synthetic");
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new BrowserArtifact(path, 9, "https://other.example:443"));
        };

        await Assert.ThrowsAsync<BridgeSnapshotException>(() =>
            harness.Bridge.DownloadStatementAsync(
                "#export",
                expected,
                DownloadStatementOptions.Create(harness.Directory, 4096, TimeSpan.FromSeconds(1)),
                Token));

        Assert.True(harness.Driver.Closed);
    }

    [Fact]
    public async Task SnapshotSwapBetweenTriggerAndClaim_FailsClosed()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.DownloadHandler = async (request, verify, token) =>
        {
            harness.Driver.CurrentSnapshot =
                TrustedBrowserBridgeCredentialTests.Snapshot(loaderId: "loader-2", securityVersion: 2);
            await verify(token);
            var path = Path.Combine(request.Options.DestinationDirectory, "transactions.csv");
            await File.WriteAllTextAsync(path, "synthetic", token);
            return new BrowserArtifact(path, new FileInfo(path).Length, request.ExpectedSnapshot.Origin);
        };

        await Assert.ThrowsAsync<BridgeSnapshotException>(() =>
            harness.Bridge.DownloadStatementAsync(
                "#export",
                expected,
                DownloadStatementOptions.Create(harness.Directory, 4096, TimeSpan.FromSeconds(1)),
                Token));

        Assert.True(harness.Driver.Closed);
    }

    [Fact]
    public async Task CancellationDuringTheDownload_ClosesTheSession_AndTheArtifactOriginNeverLeaks()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        using var cancellation = new CancellationTokenSource();
        harness.Driver.DownloadHandler = async (request, _, _) =>
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Bridge.DownloadStatementAsync(
                "#export",
                expected,
                DownloadStatementOptions.Create(harness.Directory, 4096, TimeSpan.FromSeconds(1)),
                cancellation.Token));

        Assert.True(harness.Driver.Closed);
        Assert.Empty(harness.Logger.Entries);
    }

    [Theory]
    [InlineData("", 4096)]
    [InlineData("relative/dir", 4096)]
    [InlineData("/tmp/flushedyo-download-tests", 0)]
    public void CallerOptions_RejectUnsafeDirectoryOrCap(string directory, long maxBytes)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            DownloadStatementOptions.Create(directory, maxBytes, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CallerOptions_RejectANonPositiveWait()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            DownloadStatementOptions.Create(Path.GetTempPath(), 4096, TimeSpan.Zero));
    }
}
