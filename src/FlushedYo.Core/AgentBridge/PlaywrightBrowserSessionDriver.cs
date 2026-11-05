using System.Threading.Channels;
using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Owns one isolated Chromium context over Playwright's default pipe transport. It exposes no raw
/// browser object, persistent profile, artifact capture, script-evaluation operation, or listener.
/// </summary>
internal sealed partial class PlaywrightBrowserSessionDriver : IBrowserSessionDriver, IBrowserSecurityStateSource, IDisposable
{
    private readonly ProviderBrowserPolicy _policy;
    private readonly AgentBridgeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _forceClose = new();
    private readonly object _abandonedTasksLock = new();
    private readonly List<Task> _abandonedTasks = [];
    // Only the first extra target matters: it already faults the session, so a small bound is enough and
    // a page that floods targets cannot grow this queue (TryWrite fails when full, which faults).
    private readonly Channel<BrowserEventCommand> _browserEvents =
        Channel.CreateBounded<BrowserEventCommand>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private readonly object _faultLock = new();

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private ICDPSession? _cdp;
    private BrowserSecuritySnapshotReader? _snapshotReader;
    private Task? _browserEventWorker;
    private string? _originalTargetId;
    private long _securityVersion;
    private int _freezeDocumentNavigations;
    private int _faulted;
    private int _started;
    private int _closed;
    private int _disposed;
    private volatile string? _submitOriginPin;
    private volatile bool _closeFailed;
    private volatile string _faultReason = "browser state";

    internal PlaywrightBrowserSessionDriver(
        ProviderBrowserPolicy policy,
        AgentBridgeOptions options,
        TimeProvider timeProvider)
    {
        _policy = policy;
        _options = options;
        _timeProvider = timeProvider;
    }

    public long SecurityVersion => Interlocked.Read(ref _securityVersion);

    public bool IsFaulted =>
        Volatile.Read(ref _faulted) != 0 ||
        _browser is { IsConnected: false } ||
        _page is { IsClosed: true };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsPlaywrightDebugLoggingEnabled())
        {
            throw new BridgeOperationException(
                "Playwright debug logging is enabled and could record credentials; unset PWDEBUG and DEBUG.");
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new BridgeOperationException("The isolated browser session has already been started.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _browser = await AwaitBrowserTaskAsync(
                _playwright.Chromium.LaunchAsync(BuildLaunchOptions(_options)),
                cancellationToken).ConfigureAwait(false);

            _context = await AwaitBrowserTaskAsync(
                _browser.NewContextAsync(BuildContextOptions()),
                cancellationToken).ConfigureAwait(false);

            _page = await AwaitBrowserTaskAsync(_context.NewPageAsync(), cancellationToken).ConfigureAwait(false);
            _page.SetDefaultTimeout(Milliseconds(_options.ActionTimeout));
            _page.SetDefaultNavigationTimeout(Milliseconds(_options.NavigationTimeout));
            _cdp = await AwaitBrowserTaskAsync(_context.NewCDPSessionAsync(_page), cancellationToken).ConfigureAwait(false);
            await SendAsync("Page.enable", parameters: null, cancellationToken).ConfigureAwait(false);
            await SendAsync(
                "Target.setDiscoverTargets",
                new Dictionary<string, object> { ["discover"] = true },
                cancellationToken).ConfigureAwait(false);

            var target = await SendAsync("Target.getTargetInfo", parameters: null, cancellationToken).ConfigureAwait(false);
            _originalTargetId = ReadString(ReadProperty(target, "targetInfo"), "targetId");
            if (string.IsNullOrEmpty(_originalTargetId) ||
                ReadString(ReadProperty(target, "targetInfo"), "type") != "page")
            {
                throw new BridgeSnapshotException("The initial browser target is ambiguous.");
            }

            _snapshotReader = new BrowserSecuritySnapshotReader(this, _policy, _originalTargetId);
            SubscribeToSecurityEvents();
            _browserEventWorker = ProcessBrowserEventsAsync();
            await AwaitBrowserTaskAsync(_context.RouteAsync("**/*", RouteAsync), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            await CloseFailedStartAsync().ConfigureAwait(false);
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new BridgeOperationException(
                "Chromium could not be started safely. Install the Playwright Chromium browser or configure AgentBridge:BrowserExecutablePath with AllowBrowserExecutableOverride.");
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _freezeDocumentNavigations, 0);
        var closeTask = CloseCoreAsync();
        try
        {
            await closeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ForceClose();
            await closeTask.ConfigureAwait(false);
            throw new BridgeOperationException("The browser teardown timed out; the browser process was forcibly closed.");
        }
        finally
        {
            ForceClose();
            Dispose();
        }

        if (_closeFailed)
        {
            throw new BridgeOperationException("The isolated browser teardown did not complete cleanly.");
        }
    }

    public void ForceClose()
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _forceClose.Cancel();
            }

            _playwright?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // A repeated force-close after teardown has nothing left to cancel.
        }
        catch (Exception)
        {
            _closeFailed = true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _forceClose.Dispose();
        }
    }

    // Playwright's in-process API logging (DEBUG=pw:api) records every call argument, including the
    // value passed to fill; PWDEBUG also attaches the inspector. Neither is acceptable around credentials.
    private static bool IsPlaywrightDebugLoggingEnabled() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PWDEBUG")) ||
        RequestsPlaywrightApiLogging(Environment.GetEnvironmentVariable("DEBUG"));

    private static bool RequestsPlaywrightApiLogging(string? debug)
    {
        if (string.IsNullOrEmpty(debug))
        {
            return false;
        }

        return debug.Contains("pw:", StringComparison.OrdinalIgnoreCase) || debug.Contains('*', StringComparison.Ordinal);
    }

    private async Task CloseCoreAsync()
    {
        _browserEvents.Writer.TryComplete();
        if (_browserEventWorker is not null)
        {
            await ObserveCloseStepAsync(_browserEventWorker).ConfigureAwait(false);
        }

        if (_cdp is not null)
        {
            await ObserveCloseStepAsync(_cdp.DetachAsync()).ConfigureAwait(false);
        }

        if (_context is not null)
        {
            await ObserveCloseStepAsync(_context.CloseAsync()).ConfigureAwait(false);
        }

        if (_browser is not null)
        {
            await ObserveCloseStepAsync(_browser.CloseAsync()).ConfigureAwait(false);
        }

        Task[] abandoned;
        lock (_abandonedTasksLock)
        {
            abandoned = [.. _abandonedTasks];
            _abandonedTasks.Clear();
        }

        if (abandoned.Length > 0)
        {
            await ObserveCloseStepAsync(Task.WhenAll(abandoned)).ConfigureAwait(false);
        }
    }

    private async Task ObserveCloseStepAsync(Task task)
    {
        try
        {
            await task.WaitAsync(_forceClose.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_forceClose.IsCancellationRequested)
        {
            // Forced close intentionally abandons the Playwright wait after disposing its transport.
        }
        catch (Exception)
        {
            _closeFailed = true;
        }
    }

    private async Task CloseFailedStartAsync()
    {
        try
        {
            using var teardown = new CancellationTokenSource(_options.TeardownTimeout, _timeProvider);
            await CloseAsync(teardown.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            ForceClose();
        }
    }

    private Task AwaitBrowserTaskAsync(Task task, CancellationToken cancellationToken) =>
        AwaitBrowserTaskAsync(task, _options.ActionTimeout, cancellationToken);

    private async Task AwaitBrowserTaskAsync(
        Task task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await task.WaitAsync(timeout, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            TrackIfIncomplete(task);
            throw;
        }
    }

    private Task<T> AwaitBrowserTaskAsync<T>(Task<T> task, CancellationToken cancellationToken) =>
        AwaitBrowserTaskAsync(task, _options.ActionTimeout, cancellationToken);

    private async Task<T> AwaitBrowserTaskAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await task.WaitAsync(timeout, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            TrackIfIncomplete(task);
            throw;
        }
    }

    private void TrackIfIncomplete(Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        lock (_abandonedTasksLock)
        {
            _abandonedTasks.Add(task);
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        if (Volatile.Read(ref _started) == 0 || IsFaulted)
        {
            throw new BridgeSnapshotException($"The isolated browser session is unavailable or faulted ({_faultReason}).");
        }
    }

    private IBrowser RequiredBrowser() => _browser ?? throw new BridgeOperationException("The browser is not started.");

    private IBrowserContext RequiredContext() => _context ?? throw new BridgeOperationException("The browser context is not started.");

    private IPage RequiredPage() => _page ?? throw new BridgeOperationException("The browser page is not started.");

    private ICDPSession RequiredCdp() => _cdp ?? throw new BridgeOperationException("The browser security channel is not started.");

    private static float Milliseconds(TimeSpan timeout) => (float)timeout.TotalMilliseconds;

    internal static BrowserTypeLaunchOptions BuildLaunchOptions(AgentBridgeOptions options) => new()
    {
        Headless = options.Headless,
        ExecutablePath = options.BrowserExecutablePath,
        ChromiumSandbox = true,
        Env = CreateBrowserEnvironment(),
        Timeout = Milliseconds(options.ActionTimeout),
        Args =
        [
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-default-apps",
            "--disable-extensions",
            "--disable-sync",
            "--no-first-run",
        ],
    };

    internal static BrowserNewContextOptions BuildContextOptions() => new()
    {
        AcceptDownloads = false,
        ServiceWorkers = ServiceWorkerPolicy.Block,
    };

    internal static Dictionary<string, string> CreateBrowserEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[]
                 {
                     "PATH", "HOME", "USER", "LOGNAME", "SHELL", "TMPDIR", "TEMP", "TMP",
                     "LANG", "LC_ALL", "DISPLAY", "WAYLAND_DISPLAY", "SystemRoot", "WINDIR",
                     "COMSPEC", "PATHEXT", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "PROGRAMDATA",
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }

        environment["NO_COLOR"] = "1";
        return environment;
    }

    public async Task EnterCredentialAsync(
        string cssSelector,
        string secret,
        bool submit,
        Func<CancellationToken, Task> verifySecurityState,
        Func<CancellationToken, Task> verifyAfterSubmit,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var operation = new CredentialEntryOperation(
            new CredentialEntryPlan(cssSelector, submit, verifySecurityState, verifyAfterSubmit));
        try
        {
            await RunCredentialStepsAsync(operation, secret, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Fault("browser state");
            throw;
        }
        catch (BridgeException)
        {
            Fault("browser state");
            throw;
        }
        catch (Exception)
        {
            Fault("browser state");
            throw new BridgeOperationException($"The credential {operation.Step} failed safely ({_faultReason}).");
        }
        finally
        {
            _submitOriginPin = null;
            await DisposeElementAsync(operation).ConfigureAwait(false);
        }
    }
}
