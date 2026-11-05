using FlushedYo.Core.AgentBridge;

namespace FlushedYo.IntegrationTests.Browser;

[Collection("RealBrowser")]
public sealed class BrowserDownloadTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string SyntheticSecret = "SYNTHETIC-REAL-BROWSER-SECRET";

    [Fact]
    public async Task ExportClick_LandsTheArtifactInTheCallerDirectory()
    {
        await using var harness = await DownloadHarness.CreateAsync(new DownloadScenario());
        var artifact = await harness.DownloadAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal("http://127.0.0.1:" + harness.Site.Port, artifact.Origin);
        Assert.Equal(harness.Destination, Path.GetDirectoryName(artifact.Path));
        Assert.Equal(DownloadHarness.CsvContent, await ReadTextAsync(artifact.Path));
        Assert.True(artifact.SizeBytes > 0);
    }

    [Fact]
    public async Task SuggestedFileNameWithATraversalSegment_StaysInsideTheCallerDirectory()
    {
        await using var harness = await DownloadHarness.CreateAsync(new DownloadScenario(FileName: "../escape.csv"));

        var artifact = await harness.DownloadAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(harness.Destination, Path.GetDirectoryName(artifact.Path));
        Assert.DoesNotContain("..", Path.GetFileName(artifact.Path), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(harness.Destination, "..", "escape.csv")));
    }

    /// <summary>
    /// The edge case that was open before unit 011: the export click replaces the top-level document
    /// instead of answering with an attachment. The trigger must not hang, must fail closed, and must
    /// leave nothing behind in the caller's directory.
    /// </summary>
    [Fact]
    public async Task ExportClickThatNavigatesAwayInstead_FailsClosedWithoutLeavingAnArtifact()
    {
        await using var harness = await DownloadHarness.CreateAsync(new DownloadScenario(
            ClickScript: """document.getElementById('export').addEventListener('click', () => { location.href = '/landing'; });"""));

        var exception = await Assert.ThrowsAnyAsync<BridgeException>(async () =>
            await harness.DownloadAsync(TimeSpan.FromSeconds(2), Token));

        Assert.DoesNotContain(SyntheticSecret, exception.ToString(), StringComparison.Ordinal);
        AssertNoArtifact(harness.Destination);
    }

    [Fact]
    public async Task ArtifactAboveTheByteCap_FailsClosedAndRemovesTheFile()
    {
        await using var harness = await DownloadHarness.CreateAsync(new DownloadScenario(MaxBytes: 8));

        await Assert.ThrowsAnyAsync<BridgeException>(async () =>
            await harness.DownloadAsync(TimeSpan.FromSeconds(10), Token));

        AssertNoArtifact(harness.Destination);
    }

    [Fact]
    public async Task CancellationDuringTheClaim_ClosesTheSessionAndLeavesNoArtifact()
    {
        await using var harness = await DownloadHarness.CreateAsync(new DownloadScenario());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await harness.DownloadAsync(TimeSpan.FromSeconds(10), cancellation.Token));

        AssertNoArtifact(harness.Destination);
    }

    [Fact]
    public async Task DisposalRacingAnActiveDownload_DoesNotHangAndLeavesNoArtifact()
    {
        var harness = await DownloadHarness.CreateAsync(new DownloadScenario());
        try
        {
            await harness.NavigateAsync("/account");

            var download = harness.Bridge.DownloadStatementAsync(
                "#export",
                harness.Snapshot,
                harness.Options(TimeSpan.FromSeconds(10)),
                Token);

            await Task.WhenAll(Ignore(download), harness.DisposeAsync().AsTask());

            Assert.False(download.IsCompletedSuccessfully);
            AssertNoArtifact(harness.Destination);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    private sealed record DownloadScenario(
        string FileName = "transactions.csv",
        long MaxBytes = 4096,
        string? ClickScript = null);

    private static void AssertNoArtifact(string destination) =>
        Assert.True(
            !Directory.Exists(destination)
            || !Directory.EnumerateFileSystemEntries(destination).Any(),
            "The download left an artifact in the caller directory.");

    private static async Task<string> ReadTextAsync(string artifactPath)
    {
        var bytes = await File.ReadAllBytesAsync(artifactPath, Token);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async Task Ignore(Task task)
    {
        try
        {
            await task;
        }
        catch (BridgeException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string ExportPage(string markup, string script) => $$"""
        <!doctype html><title>Account</title>
        {{markup}}
        <script>{{script}}</script>
        """;

    private sealed class DownloadHarness : IAsyncDisposable
    {
        internal const string CsvContent = "date,amount\n2026-01-15,-12.34\n";

        private TrustedBrowserBridge? _bridge;
        private int _disposed;

        private DownloadHarness(SyntheticSite site, string destination, long maxBytes)
        {
            Site = site;
            Destination = destination;
            MaxBytes = maxBytes;
        }

        internal SyntheticSite Site { get; }

        internal string Destination { get; }

        internal TrustedBrowserBridge Bridge => _bridge!;

        internal BrowserSecuritySnapshot Snapshot { get; private set; } = null!;

        internal static async Task<DownloadHarness> CreateAsync(DownloadScenario scenario)
        {
            BrowserTestEnvironment.RequireChromium();
            var site = new SyntheticSite();
            site.MapHtml("/account", ExportPage(
                markup: """<button id="export">Export</button>""",
                script: scenario.ClickScript
                    ?? """document.getElementById('export').addEventListener('click', () => { location.href = '/export'; });"""));
            site.MapDownload("/export", new DownloadResponse(scenario.FileName, CsvContent));
            site.MapHtml("/landing", "<!doctype html><title>Landing</title>");
            var harness = new DownloadHarness(
                site,
                System.IO.Directory.CreateTempSubdirectory("flushedyo-artifact-").FullName,
                scenario.MaxBytes);
            harness._bridge = site.CreateBridge();
            await harness.NavigateAsync("/account");
            return harness;
        }

        internal long MaxBytes { get; }

        internal async Task NavigateAsync(string path)
        {
            await Bridge.NavigateAsync(Site.Url(path), Token);
            Snapshot = await Bridge.GetSecuritySnapshotAsync(Token);
        }

        internal DownloadStatementOptions Options(TimeSpan timeout) =>
            DownloadStatementOptions.Create(Destination, MaxBytes, timeout);

        internal Task<BrowserArtifact> DownloadAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Bridge.DownloadStatementAsync("#export", Snapshot, Options(timeout), cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_bridge is not null)
            {
                try
                {
                    await _bridge.DisposeAsync();
                }
                catch (BridgeException)
                {
                }

                _bridge = null;
            }

            await Site.DisposeAsync();
            FlushedYo.Core.AgentBridge.ArtifactCopy.DeleteDirectory(Destination);
        }
    }
}
