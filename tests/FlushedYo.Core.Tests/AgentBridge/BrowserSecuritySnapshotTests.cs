using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class BrowserSecuritySnapshotTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SpoofedTargetUrlMismatch_FailsClosed()
    {
        var source = Source(ValidState() with { TargetUrl = "https://evil.example/login" });

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(source).ReadAsync(Token));
    }

    [Fact]
    public async Task SpoofedFrameUrlMismatch_FailsClosed()
    {
        var source = Source(ValidState() with { FrameUrl = "https://evil.example/login" });

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(source).ReadAsync(Token));
    }

    [Fact]
    public async Task SpoofedNavigationHistoryMismatch_FailsClosed()
    {
        var source = Source(ValidState() with { NavigationHistoryUrl = "https://evil.example/login" });

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(source).ReadAsync(Token));
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 1, 2)]
    public async Task ExtraPageOrTarget_Faults(int pageTargets, int totalTargets, int contextPages)
    {
        var source = Source(ValidState() with
        {
            PageTargetCount = pageTargets,
            TotalTargetCount = totalTargets,
            ContextPageCount = contextPages,
        });

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(source).ReadAsync(Token));
    }

    [Fact]
    public async Task SecurityVersionChangesDuringRead_AreRetriedUntilStable()
    {
        var source = Source(ValidState());
        source.VersionChangesRemaining = 2;

        var snapshot = await Reader(source).ReadAsync(Token);

        Assert.Equal(3, source.ReadCount);
        Assert.Equal(3, snapshot.SecurityVersion);
    }

    [Fact]
    public async Task ContinuouslyChangingSecurityVersion_ExhaustsBoundedRetryAndFails()
    {
        var source = Source(ValidState());
        source.ChangeOnEveryRead = true;

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(source).ReadAsync(Token));
        Assert.Equal(4, source.ReadCount);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("type")]
    [InlineData("frame")]
    [InlineData("loader")]
    [InlineData("origin")]
    [InlineData("history")]
    public async Task MissingStructuredFields_FailClosed(string field)
    {
        var state = ValidState();
        state = field switch
        {
            "target" => state with { TargetId = null },
            "type" => state with { TargetType = null },
            "frame" => state with { TopLevelFrameId = "" },
            "loader" => state with { LoaderId = null },
            "origin" => state with { SecurityOrigin = null },
            "history" => state with { NavigationHistoryUrl = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var source = Source(state);

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(source).ReadAsync(Token));
    }

    [Fact]
    public async Task TargetTypeOrOriginalIdentityChange_FailsClosed()
    {
        var wrongType = Source(ValidState() with { TargetType = "iframe" });
        var wrongTarget = Source(ValidState() with { TargetId = "target-2" });

        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(wrongType).ReadAsync(Token));
        await Assert.ThrowsAsync<BridgeSnapshotException>(() => Reader(wrongTarget).ReadAsync(Token));
    }

    [Fact]
    public async Task SecurityOriginMustAgreeWithDocumentAndPolicy()
    {
        var source = Source(ValidState() with { SecurityOrigin = "https://other.example" });

        await Assert.ThrowsAsync<BridgePolicyException>(() => Reader(source).ReadAsync(Token));
    }

    [Fact]
    public async Task ValidStructuredState_ProducesImmutableOriginOnlySnapshot()
    {
        var snapshot = await Reader(Source(ValidState())).ReadAsync(Token);

        Assert.Equal("target-1", snapshot.TargetId);
        Assert.Equal("frame-1", snapshot.TopLevelFrameId);
        Assert.Equal("loader-1", snapshot.LoaderId);
        Assert.Equal("https://example.com:443", snapshot.Origin);
        Assert.Equal(1, snapshot.SecurityVersion);
        Assert.DoesNotContain("login", snapshot.ToString(), StringComparison.Ordinal);
    }

    private static BrowserSecuritySnapshotReader Reader(FakeSecurityStateSource source) =>
        new(source, new ProviderBrowserPolicy(["https://example.com"]), "target-1");

    private static FakeSecurityStateSource Source(BrowserSecurityState state) => new(state);

    private static BrowserSecurityState ValidState() => new()
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

    private sealed class FakeSecurityStateSource(BrowserSecurityState state) : IBrowserSecurityStateSource
    {
        private long _version = 1;

        public long SecurityVersion => Interlocked.Read(ref _version);

        internal int ReadCount { get; private set; }

        internal int VersionChangesRemaining { get; set; }

        internal bool ChangeOnEveryRead { get; set; }

        public Task<BrowserSecurityState> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (ChangeOnEveryRead || VersionChangesRemaining > 0)
            {
                Interlocked.Increment(ref _version);
                if (VersionChangesRemaining > 0)
                {
                    VersionChangesRemaining--;
                }
            }

            return Task.FromResult(state);
        }
    }
}
