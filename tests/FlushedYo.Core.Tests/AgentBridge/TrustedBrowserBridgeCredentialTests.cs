using FlushedYo.Core.AgentBridge;
using FlushedYo.Core.SecretStore;
using FlushedYo.Core.Tests.Plugin;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class TrustedBrowserBridgeCredentialTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CredentialIsFetchedInternally_AndSnapshotSurroundsEveryDomStep()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;

        await harness.Bridge.EnterCredentialAsync("website/password", "#password", expected, Token);

        AssertCredentialFetchedAndSessionOpen(harness);
        Assert.True(harness.Driver.VerificationCount >= 5);
    }

    private static void AssertCredentialFetchedAndSessionOpen(BridgeTestHarness harness)
    {
        Assert.Equal("website/password", harness.Secrets.RequestedKey);
        Assert.Equal(BridgeTestHarness.SecretValue, harness.Driver.SecretSeen);
        Assert.Equal(["focus", "fill"], harness.Driver.DomSteps);
        AssertNotClosed(harness.Bridge, harness.Driver);
    }

    [Fact]
    public async Task OriginSwapBeforeEntry_FailsBeforeSecretLookupOrInput()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.CurrentSnapshot = Snapshot(origin: "https://other.example:443");

        var exception = await Assert.ThrowsAsync<BridgeSnapshotException>(() =>
            harness.Bridge.EnterCredentialAsync("website/password", "#password", expected, Token));

        AssertNoSecretLeaked(exception);
        Assert.Equal(0, harness.Secrets.CallCount);
        Assert.Empty(harness.Driver.DomSteps);
        AssertSessionClosed(harness);
    }

    private static void AssertSessionClosed(BridgeTestHarness harness)
    {
        AssertSnapshotAndSession(harness.Bridge, harness.Driver, closed: true, frozen: false);
    }

    private static void AssertDomSteps(BridgeTestHarness harness, params string[] expected)
    {
        Assert.Equal(expected, harness.Driver.DomSteps);
    }

    private static void AssertSecretNotSeen(BridgeTestHarness harness)
    {
        Assert.Null(harness.Driver.SecretSeen);
    }

    private static void AssertFailureClosedNoSecret(BridgeTestHarness harness, params string[] steps)
    {
        AssertDomSteps(harness, steps);
        AssertSecretNotSeen(harness);
        AssertSessionClosed(harness);
    }

    private static void AssertFailureClosed(BridgeTestHarness harness, params string[] steps)
    {
        AssertDomSteps(harness, steps);
        AssertSessionClosed(harness);
    }

    private static void AssertNoSecretLeaked(Exception exception)
    {
        Assert.DoesNotContain(BridgeTestHarness.SecretValue, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapshotSwapAfterFocus_FailsBeforeFill()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.StageHook = (stage, _) =>
        {
            if (stage == "focus")
            {
                harness.Driver.CurrentSnapshot = Snapshot(loaderId: "loader-2", securityVersion: 2);
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<BridgeSnapshotException>(() =>
            harness.Bridge.EnterCredentialAsync("website/password", "#password", expected, Token));

        AssertFailureClosedNoSecret(harness, "focus");
    }

    [Fact]
    public async Task SameOriginNewDocumentAfterSubmit_Succeeds()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.StageHook = (stage, _) =>
        {
            if (stage == "submit")
            {
                harness.Driver.CurrentSnapshot = Snapshot(loaderId: "loader-2", securityVersion: 2);
            }

            return Task.CompletedTask;
        };

        await harness.Bridge.SubmitCredentialAsync(
            SubmitCredentialArgs.Create("website/password", "#password", expected), Token);

        AssertDomSteps(harness, "focus", "fill", "submit");
        AssertNotClosed(harness.Bridge, harness.Driver);
    }

    [Theory]
    [InlineData("https://evil.example:443", "target-1")]
    [InlineData("https://example.com:443", "target-2")]
    public async Task DisallowedOriginOrTargetSwapAfterSubmit_FaultsAndClosesSession(string origin, string targetId)
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.StageHook = (stage, _) =>
        {
            if (stage == "submit")
            {
                harness.Driver.CurrentSnapshot = Snapshot(targetId: targetId, origin: origin, loaderId: "loader-2", securityVersion: 2);
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<BridgeSnapshotException>(() =>
            harness.Bridge.SubmitCredentialAsync(
                SubmitCredentialArgs.Create("website/password", "#password", expected), Token));

        AssertFailureClosed(harness, "focus", "fill", "submit");
    }

    [Fact]
    public async Task SubmitWithoutObservedNavigation_IsNotReportedAsSuccess()
    {
        using var harness = new BridgeTestHarness();

        await Assert.ThrowsAsync<BridgeOperationException>(() => harness.Bridge.SubmitCredentialAsync(
            SubmitCredentialArgs.Create("website/password", "#password", harness.Driver.CurrentSnapshot),
            Token));

        AssertFailureClosed(harness, "focus", "fill", "submit");
    }

    [Fact]
    public async Task NavigationToDifferentAllowedOriginAfterSubmit_FaultsAndClosesSession()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;
        harness.Driver.StageHook = (stage, _) =>
        {
            if (stage == "submit")
            {
                harness.Driver.CurrentSnapshot = Snapshot(origin: "https://other.example:443", loaderId: "loader-2", securityVersion: 2);
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<BridgeSnapshotException>(() =>
            harness.Bridge.SubmitCredentialAsync(
                SubmitCredentialArgs.Create("website/password", "#password", expected), Token));

        AssertFailureClosed(harness, "focus", "fill", "submit");
    }

    [Fact]
    public async Task MissingSecret_IsValueFreeAndFailsClosed()
    {
        using var harness = new BridgeTestHarness(secretValue: null);

        var exception = await Assert.ThrowsAsync<BridgeOperationException>(() =>
            harness.Bridge.EnterCredentialAsync(
                "missing/key",
                "#password",
                harness.Driver.CurrentSnapshot,
                Token));

        Assert.DoesNotContain("missing/key", exception.Message, StringComparison.Ordinal);
        AssertFailureClosedNoSecret(harness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NonUniqueSelector_FailsClosedBeforeInput(int matches)
    {
        using var harness = new BridgeTestHarness();
        harness.Driver.SelectorMatches = matches;

        await Assert.ThrowsAsync<BridgeOperationException>(() =>
            harness.Bridge.EnterCredentialAsync(
                "website/password",
                "#password",
                harness.Driver.CurrentSnapshot,
                Token));

        AssertFailureClosedNoSecret(harness);
    }

    private static void AssertSnapshotAndSession(TrustedBrowserBridge bridge, FakeBrowserSessionDriver driver,
        bool closed, bool frozen)
    {
        Assert.Equal(closed, driver.Closed);
        Assert.Equal(frozen, driver.DocumentNavigationFrozen);
    }

    private static void AssertNotClosed(TrustedBrowserBridge bridge, FakeBrowserSessionDriver driver)
    {
        Assert.False(driver.Closed);
        Assert.False(driver.DocumentNavigationFrozen);
    }

    [Fact]
    public async Task CallerValidationErrors_DoNotCloseHealthySession()
    {
        using var harness = new BridgeTestHarness();
        var expected = harness.Driver.CurrentSnapshot;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Bridge.EnterCredentialAsync("", "#password", expected, Token));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Bridge.EnterCredentialAsync("website/password", " ", expected, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Bridge.EnterCredentialAsync("website/password", "#password", null!, Token));

        AssertNotClosed(harness.Bridge, harness.Driver);
        var actual = await harness.Bridge.GetSecuritySnapshotAsync(Token);
        Assert.Equal(expected, actual);
    }

    internal static BrowserSecuritySnapshot Snapshot(
        string targetId = "target-1",
        string frameId = "frame-1",
        string loaderId = "loader-1",
        string origin = "https://example.com:443",
        long securityVersion = 1) =>
        new(targetId, frameId, loaderId, origin, securityVersion);
}

internal sealed class BridgeTestHarness : IDisposable
{
    internal const string SecretValue = "SYNTHETIC-BRIDGE-SECRET";

    internal BridgeTestHarness(string? secretValue = SecretValue, TimeProvider? timeProvider = null)
    {
        Options = new AgentBridgeOptions
        {
            AllowedOrigins = ["https://example.com", "https://other.example"],
            Headless = true,
            TeardownTimeout = TimeSpan.FromSeconds(2),
            MaxMfaTimeout = TimeSpan.FromSeconds(5),
        };
        Driver = new FakeBrowserSessionDriver();
        Secrets = new FakeSecretStore(secretValue);
        Logger = new CapturingLogger<TrustedBrowserBridge>();
        Bridge = new TrustedBrowserBridge(Driver, Options, Secrets, Logger, timeProvider);
        Directory = System.IO.Directory.CreateTempSubdirectory("flushedyo-download-tests-").FullName;
    }

    internal AgentBridgeOptions Options { get; }

    internal FakeBrowserSessionDriver Driver { get; }

    internal FakeSecretStore Secrets { get; }

    internal CapturingLogger<TrustedBrowserBridge> Logger { get; }

    internal TrustedBrowserBridge Bridge { get; }

    /// <summary>Gets a caller-owned directory for artifacts, deleted with the harness.</summary>
    internal string Directory { get; }

    public void Dispose()
    {
        Bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
        FlushedYo.Core.AgentBridge.ArtifactCopy.DeleteDirectory(Directory);
    }
}

internal sealed class FakeSecretStore(string? secretValue) : ISecretStore
{
    private readonly string? _secretValue = secretValue;
    private Exception? _exception;

    internal string? RequestedKey { get; private set; }

    internal int CallCount { get; private set; }

    internal Exception Exception
    {
        set => _exception = value;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        CallCount++;
        RequestedKey = key;
        cancellationToken.ThrowIfCancellationRequested();
        if (_exception is not null)
        {
            throw _exception;
        }

        return Task.FromResult(_secretValue);
    }

    public async Task<Credential?> GetCredentialAsync(string key, CancellationToken cancellationToken = default) =>
        await GetSecretAsync(key, cancellationToken) is { } value ? new Credential(value, value) : null;

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secretValue is not null);
}

internal sealed class FakeBrowserSessionDriver : IBrowserSessionDriver
{
    private int _faulted;
    private int _verificationCount;
    private int _downloadCallCount;

    internal BrowserSecuritySnapshot CurrentSnapshot { get; set; } = TrustedBrowserBridgeCredentialTests.Snapshot();

    internal Queue<BrowserSecuritySnapshot> SnapshotQueue { get; } = new();

    internal List<string> DomSteps { get; } = [];

    internal string? SecretSeen { get; private set; }

    internal int SelectorMatches { get; set; } = 1;

    internal int VerificationCount => _verificationCount;

    internal bool DocumentNavigationFrozen { get; private set; }

    internal bool Closed { get; private set; }

    internal bool ForceClosed { get; private set; }

    internal TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Func<string, CancellationToken, Task>? StageHook { get; set; }

    internal Func<CancellationToken, Task>? CloseHook { get; set; }

    internal Func<int, CancellationToken, Task>? SnapshotHook { get; set; }

    internal Func<DownloadRequest, Func<CancellationToken, Task>, CancellationToken, Task<BrowserArtifact>>? DownloadHandler { get; set; }

    internal DownloadRequest? LastDownloadRequest { get; private set; }

    internal int DownloadCallCount => Volatile.Read(ref _downloadCallCount);

    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NavigateAsync(Uri destination, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<BrowserSecuritySnapshot> GetSecuritySnapshotAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _verificationCount);
        if (SnapshotHook is not null)
        {
            await SnapshotHook(call, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return SnapshotQueue.Count > 0 ? SnapshotQueue.Dequeue() : CurrentSnapshot;
    }

    public Task SetDocumentNavigationFrozenAsync(bool frozen, CancellationToken cancellationToken = default)
    {
        DocumentNavigationFrozen = frozen;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task EnterCredentialAsync(
        string cssSelector,
        string secret,
        bool submit,
        Func<CancellationToken, Task> verifySecurityState,
        Func<CancellationToken, Task> verifyAfterSubmit,
        CancellationToken cancellationToken = default)
    {
        await Verify(verifySecurityState, cancellationToken);
        if (SelectorMatches != 1)
        {
            throw new BridgeOperationException("The configured credential control is missing or ambiguous.");
        }

        await Step("focus", verifySecurityState, cancellationToken);
        SecretSeen = secret;
        await Step("fill", verifySecurityState, cancellationToken);
        if (submit)
        {
            await Verify(verifySecurityState, cancellationToken);
            DomSteps.Add("submit");
            if (StageHook is not null)
            {
                await StageHook("submit", cancellationToken);
            }

            await Verify(verifyAfterSubmit, cancellationToken);
        }
    }

    public async Task<BrowserArtifact> DownloadAsync(
        DownloadRequest request,
        Func<CancellationToken, Task> verifySecurityState,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _downloadCallCount);
        await Verify(verifySecurityState, cancellationToken);
        LastDownloadRequest = request;
        if (DownloadHandler is null)
        {
            throw new BridgeOperationException("The download seam is not configured.");
        }

        return await DownloadHandler(request, verifySecurityState, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (Closed)
        {
            return;
        }

        CloseStarted.TrySetResult();
        if (CloseHook is not null)
        {
            await CloseHook(cancellationToken);
        }

        Closed = true;
    }

    public void ForceClose()
    {
        ForceClosed = true;
        Closed = true;
    }

    internal void Fault() => Interlocked.Exchange(ref _faulted, 1);

    private async Task Step(
        string stage,
        Func<CancellationToken, Task> verifySecurityState,
        CancellationToken cancellationToken)
    {
        await Verify(verifySecurityState, cancellationToken);
        DomSteps.Add(stage);
        if (StageHook is not null)
        {
            await StageHook(stage, cancellationToken);
        }

        await Verify(verifySecurityState, cancellationToken);
    }

    private static async Task Verify(Func<CancellationToken, Task> verifySecurityState, CancellationToken cancellationToken)
    {
        await verifySecurityState(cancellationToken);
    }
}
