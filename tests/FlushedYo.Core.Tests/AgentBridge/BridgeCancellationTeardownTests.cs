using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Core.Tests.AgentBridge;

public sealed class BridgeCancellationTeardownTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CancellationDuringEntry_UnwindsInsideGateAndClosesBrowser()
    {
        using var harness = new BridgeTestHarness();
        var fillStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Driver.StageHook = async (stage, token) =>
        {
            if (stage == "fill")
            {
                fillStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var entry = harness.Bridge.EnterCredentialAsync(
            "website/password",
            "#password",
            harness.Driver.CurrentSnapshot,
            cancellation.Token);
        await fillStarted.Task.WaitAsync(Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entry);
        Assert.True(harness.Driver.Closed);
        await Assert.ThrowsAsync<BridgeOperationException>(() => harness.Bridge.GetSecuritySnapshotAsync(Token));
    }

    [Fact]
    public async Task DisposeWaitsForInflightOperation_AndOperationFinishesTeardownBeforeGateRelease()
    {
        using var harness = new BridgeTestHarness();
        var fillStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Driver.StageHook = async (stage, token) =>
        {
            if (stage == "fill")
            {
                fillStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        harness.Driver.CloseHook = async token =>
        {
            closeBlocked.TrySetResult();
            await closeMayFinish.Task.WaitAsync(token);
        };

        var entry = harness.Bridge.EnterCredentialAsync(
            "website/password",
            "#password",
            harness.Driver.CurrentSnapshot,
            Token);
        await fillStarted.Task.WaitAsync(Token);
        var disposal = harness.Bridge.DisposeAsync().AsTask();
        await closeBlocked.Task.WaitAsync(Token);

        Assert.False(disposal.IsCompleted);
        closeMayFinish.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entry);
        await disposal.WaitAsync(Token);
        Assert.True(harness.Driver.Closed);
    }

    [Fact]
    public async Task DisposalAfterCancellation_ObservesFaultWithoutRacingEntry()
    {
        using var harness = new BridgeTestHarness();
        var fillStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Driver.StageHook = async (stage, token) =>
        {
            if (stage == "fill")
            {
                fillStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entry = harness.Bridge.EnterCredentialAsync(
            "website/password",
            "#password",
            harness.Driver.CurrentSnapshot,
            cancellation.Token);
        await fillStarted.Task.WaitAsync(Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entry);

        await harness.Bridge.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Bridge.GetSecuritySnapshotAsync(Token));
    }

    [Fact]
    public async Task TeardownTimeout_UsesInjectedTimeProviderAndForcesClose()
    {
        var time = new ManualTimeProvider();
        using var harness = new BridgeTestHarness(timeProvider: time);
        harness.Driver.CloseHook = async token => await Task.Delay(Timeout.InfiniteTimeSpan, token);

        var disposal = harness.Bridge.DisposeAsync().AsTask();
        await harness.Driver.CloseStarted.Task.WaitAsync(Token);
        time.Advance(harness.Options.TeardownTimeout);

        await Assert.ThrowsAsync<BridgeOperationException>(() => disposal.WaitAsync(Token));
        Assert.True(harness.Driver.ForceClosed);
    }

    [Fact]
    public async Task EveryCallAfterDispose_ThrowsObjectDisposedException()
    {
        using var harness = new BridgeTestHarness();
        await harness.Bridge.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Bridge.NavigateAsync(new Uri("https://example.com"), Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Bridge.GetSecuritySnapshotAsync(Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Bridge.EnterCredentialAsync(
            "website/password",
            "#password",
            harness.Driver.CurrentSnapshot,
            Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Bridge.WaitForManualMfaAsync(
            new Uri("https://example.com"),
            TimeSpan.FromSeconds(1),
            Token));
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (_sync)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        TimerCreated.TrySetResult();
        return timer;
    }

    internal void Advance(TimeSpan amount)
    {
        ManualTimer[] due;
        lock (_sync)
        {
            _now += amount;
            due = _timers.Where(timer => timer.IsDue(_now)).ToArray();
            foreach (var timer in due)
            {
                timer.ScheduleNext(_now);
            }
        }

        foreach (var timer in due)
        {
            timer.Invoke();
        }
    }

    private DateTimeOffset Now
    {
        get
        {
            lock (_sync)
            {
                return _now;
            }
        }
    }

    private sealed class ManualTimer(
        ManualTimeProvider provider,
        TimerCallback callback,
        object? state) : ITimer
    {
        private readonly object _sync = new();
        private DateTimeOffset? _due;
        private TimeSpan _period;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : provider.Now + dueTime;
                return true;
            }
        }

        internal bool IsDue(DateTimeOffset now)
        {
            lock (_sync)
            {
                return !_disposed && _due <= now;
            }
        }

        internal void ScheduleNext(DateTimeOffset now)
        {
            lock (_sync)
            {
                _due = _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan
                    ? now + _period
                    : null;
            }
        }

        internal void Invoke() => callback(state);

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _due = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
