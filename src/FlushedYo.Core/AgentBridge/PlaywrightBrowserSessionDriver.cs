using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Owns one isolated Chromium context over Playwright's default pipe transport. It exposes no raw
/// browser object, persistent profile, artifact capture, script-evaluation operation, or listener.
/// </summary>
internal sealed class PlaywrightBrowserSessionDriver : IBrowserSessionDriver, IBrowserSecurityStateSource, IDisposable
{
    private readonly ProviderBrowserPolicy _policy;
    private readonly AgentBridgeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _forceClose = new();
    private readonly object _abandonedTasksLock = new();
    private readonly List<Task> _abandonedTasks = [];
    private readonly Channel<BrowserEventCommand> _browserEvents =
        Channel.CreateUnbounded<BrowserEventCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

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
    private bool _closeFailed;
    private string _faultReason = "browser state";

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
                _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _options.Headless,
                    ExecutablePath = _options.BrowserExecutablePath,
                    ChromiumSandbox = true,
                    Env = CreateBrowserEnvironment(),
                    Timeout = Milliseconds(_options.ActionTimeout),
                    Args =
                    [
                        "--disable-background-networking",
                        "--disable-component-update",
                        "--disable-default-apps",
                        "--disable-extensions",
                        "--disable-sync",
                        "--no-first-run",
                    ],
                }),
                cancellationToken).ConfigureAwait(false);

            _context = await AwaitBrowserTaskAsync(
                _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    AcceptDownloads = false,
                    ServiceWorkers = ServiceWorkerPolicy.Block,
                }),
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
            _originalTargetId = ReadString(target, "targetInfo", "targetId");
            if (string.IsNullOrEmpty(_originalTargetId) ||
                ReadString(target, "targetInfo", "type") != "page")
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
                "Chromium could not be started safely. Install the Playwright Chromium browser or configure AgentBridge:BrowserExecutablePath.");
        }
    }

    public async Task NavigateAsync(Uri destination, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        if (!_policy.IsAllowed(destination))
        {
            Fault("browser state");
            throw new BridgePolicyException("The browser destination origin is not allowed.");
        }

        try
        {
            await AwaitBrowserTaskAsync(
                RequiredPage().GotoAsync(
                    destination.AbsoluteUri,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.Load,
                        Timeout = Milliseconds(_options.NavigationTimeout),
                    }),
                _options.NavigationTimeout,
                cancellationToken).ConfigureAwait(false);
            ThrowIfUnavailable();
        }
        catch (OperationCanceledException)
        {
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
            throw new BridgeOperationException("The browser navigation failed or was blocked.");
        }
    }

    public Task<BrowserSecuritySnapshot> GetSecuritySnapshotAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        return RequiredSnapshotReader().ReadAsync(cancellationToken);
    }

    public Task SetDocumentNavigationFrozenAsync(bool frozen, CancellationToken cancellationToken = default)
    {
        if (frozen)
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
        }

        Interlocked.Exchange(ref _freezeDocumentNavigations, frozen ? 1 : 0);
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
        ThrowIfUnavailable();
        IElementHandle? element = null;
        var operationStep = "security verification";
        try
        {
            await verifySecurityState(cancellationToken).ConfigureAwait(false);
            operationStep = "selector resolution";
            var elements = await AwaitBrowserTaskAsync(
                RequiredPage().QuerySelectorAllAsync(cssSelector),
                cancellationToken).ConfigureAwait(false);
            if (elements.Count != 1)
            {
                throw new BridgeOperationException("The configured credential control is missing or ambiguous.");
            }

            element = elements[0];
            operationStep = "security verification";
            await verifySecurityState(cancellationToken).ConfigureAwait(false);
            operationStep = "focus";
            await AwaitBrowserTaskAsync(element.FocusAsync(), cancellationToken).ConfigureAwait(false);
            operationStep = "security verification";
            await verifySecurityState(cancellationToken).ConfigureAwait(false);

            await verifySecurityState(cancellationToken).ConfigureAwait(false);
            operationStep = "fill";
            await AwaitBrowserTaskAsync(element.FillAsync(secret), cancellationToken).ConfigureAwait(false);
            operationStep = "security verification";
            await verifySecurityState(cancellationToken).ConfigureAwait(false);

            if (submit)
            {
                await verifySecurityState(cancellationToken).ConfigureAwait(false);
                operationStep = "submission";
                // Submission is the one document navigation the bridge itself initiates. Disallowed
                // destinations are still aborted by the route policy, and the post-submit check
                // requires the same target and frame at an allowed origin.
                Interlocked.Exchange(ref _freezeDocumentNavigations, 0);
                await AwaitBrowserTaskAsync(element.PressAsync("Enter"), cancellationToken).ConfigureAwait(false);
                operationStep = "post-submit verification";
                await verifyAfterSubmit(cancellationToken).ConfigureAwait(false);
            }
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
            throw new BridgeOperationException($"The credential {operationStep} failed safely.");
        }
        finally
        {
            if (element is not null)
            {
                try
                {
                    await element.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    Fault("browser state");
                }
            }
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

    async Task<BrowserSecurityState> IBrowserSecurityStateSource.ReadAsync(CancellationToken cancellationToken)
    {
        var target = await SendAsync("Target.getTargetInfo", parameters: null, cancellationToken).ConfigureAwait(false);
        var tree = await SendAsync("Page.getFrameTree", parameters: null, cancellationToken).ConfigureAwait(false);
        var history = await SendAsync("Page.getNavigationHistory", parameters: null, cancellationToken).ConfigureAwait(false);
        var targets = await SendAsync("Target.getTargets", parameters: null, cancellationToken).ConfigureAwait(false);

        var frame = ReadProperty(ReadProperty(tree, "frameTree"), "frame");
        var historyEntry = ReadCurrentHistoryEntry(history);
        var targetInfos = ReadProperty(targets, "targetInfos");
        var context = RequiredContext();
        var page = RequiredPage();

        return new BrowserSecurityState
        {
            TargetId = ReadString(target, "targetInfo", "targetId"),
            TargetType = ReadString(target, "targetInfo", "type"),
            TargetUrl = ReadString(target, "targetInfo", "url"),
            TopLevelFrameId = ReadString(frame, "id"),
            LoaderId = ReadString(frame, "loaderId"),
            FrameUrl = ReadString(frame, "url"),
            SecurityOrigin = ReadString(frame, "securityOrigin"),
            IsTopLevelFrame = frame.ValueKind == JsonValueKind.Object && !frame.TryGetProperty("parentId", out _),
            NavigationHistoryUrl = ReadString(historyEntry, "url"),
            PageTargetCount = CountPageTargets(targetInfos),
            TotalTargetCount = CountSecurityRelevantTargets(targetInfos),
            ContextPageCount = context.Pages.Count,
            IsOriginalPage = context.Pages.Count == 1 && ReferenceEquals(context.Pages[0], page),
            BrowserConnected = RequiredBrowser().IsConnected,
            PageClosed = page.IsClosed,
        };
    }

    private async Task RouteAsync(IRoute route)
    {
        ChangeSecurityState();
        try
        {
            var request = route.Request;
            var isDocument = request.ResourceType == "document";
            var frozen = isDocument && Volatile.Read(ref _freezeDocumentNavigations) != 0;
            var disallowed = isDocument &&
                             (!Uri.TryCreate(request.Url, UriKind.Absolute, out var requestUrl) || !_policy.IsAllowed(requestUrl));
            if (frozen || disallowed)
            {
                Fault("blocked document request");
                await route.AbortAsync("BlockedByClient").ConfigureAwait(false);
                return;
            }

            await route.ContinueAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            Fault("route failure");
            try
            {
                await route.AbortAsync("BlockedByClient").ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The protected operation observes the fault; browser diagnostics never escape.
            }
        }
    }

    private void SubscribeToSecurityEvents()
    {
        RequiredBrowser().Disconnected += (_, _) => Fault("browser disconnected");
        RequiredContext().Page += (_, page) =>
        {
            ChangeSecurityState();
            if (!ReferenceEquals(page, _page))
            {
                Fault("additional page");
                Enqueue(BrowserEventCommand.ForPage(page));
            }
        };

        var page = RequiredPage();
        page.Crash += (_, _) => Fault("page crash");
        page.Close += (_, _) =>
        {
            if (Volatile.Read(ref _closed) == 0)
            {
                Fault("page closed");
            }
        };
        page.FrameNavigated += (_, _) => ChangeSecurityState();
        page.FrameAttached += (_, _) => ChangeSecurityState();
        page.FrameDetached += (_, _) => ChangeSecurityState();

        var cdp = RequiredCdp();
        cdp.Event("Page.frameNavigated").OnEvent += (_, _) => ChangeSecurityState();
        cdp.Event("Page.frameDetached").OnEvent += (_, _) => ChangeSecurityState();
        cdp.Event("Target.targetCreated").OnEvent += (_, data) => OnTargetCreated(data);
        cdp.Event("Target.targetInfoChanged").OnEvent += (_, _) => ChangeSecurityState();
        cdp.Event("Target.targetDestroyed").OnEvent += (_, _) => ChangeSecurityState();
    }

    private void OnTargetCreated(JsonElement? data)
    {
        ChangeSecurityState();
        if (data is not { ValueKind: JsonValueKind.Object } payload)
        {
            Fault("invalid target event");
            return;
        }

        var targetId = ReadString(payload, "targetInfo", "targetId");
        var targetType = ReadString(payload, "targetInfo", "type");
        if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(targetType))
        {
            Fault("incomplete target event");
            return;
        }

        if (targetType == "browser")
        {
            return;
        }

        if (!string.Equals(targetId, _originalTargetId, StringComparison.Ordinal))
        {
            Fault($"additional {targetType} target");
            Enqueue(BrowserEventCommand.ForTarget(targetId));
        }
    }

    private async Task ProcessBrowserEventsAsync()
    {
        await foreach (var command in _browserEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (command.Page is not null)
                {
                    await command.Page.CloseAsync().ConfigureAwait(false);
                }
                else if (command.TargetId is not null && _cdp is not null)
                {
                    await _cdp.SendAsync(
                        "Target.closeTarget",
                        new Dictionary<string, object> { ["targetId"] = command.TargetId }).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                Fault("browser state");
            }
        }
    }

    private void Enqueue(BrowserEventCommand command)
    {
        if (Volatile.Read(ref _closed) == 0 && !_browserEvents.Writer.TryWrite(command))
        {
            Fault("browser state");
        }
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

    private async Task<JsonElement> SendAsync(
        string method,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var result = await AwaitBrowserTaskAsync(
            RequiredCdp().SendAsync(method, parameters),
            cancellationToken).ConfigureAwait(false);
        return result ?? throw new BridgeSnapshotException("The browser returned no structured security metadata.");
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

    private void Fault(string reason)
    {
        _faultReason = reason;
        Interlocked.Exchange(ref _faulted, 1);
        ChangeSecurityState();
    }

    private void ChangeSecurityState() => Interlocked.Increment(ref _securityVersion);

    private IBrowser RequiredBrowser() => _browser ?? throw new BridgeOperationException("The browser is not started.");

    private IBrowserContext RequiredContext() => _context ?? throw new BridgeOperationException("The browser context is not started.");

    private IPage RequiredPage() => _page ?? throw new BridgeOperationException("The browser page is not started.");

    private ICDPSession RequiredCdp() => _cdp ?? throw new BridgeOperationException("The browser security channel is not started.");

    private BrowserSecuritySnapshotReader RequiredSnapshotReader() =>
        _snapshotReader ?? throw new BridgeOperationException("The browser snapshot reader is not started.");

    private static float Milliseconds(TimeSpan timeout) => (float)timeout.TotalMilliseconds;

    private static Dictionary<string, string> CreateBrowserEnvironment()
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

    private static JsonElement ReadProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value
            : default;

    private static string? ReadString(JsonElement element, string parent, string property) =>
        ReadString(ReadProperty(element, parent), property);

    private static string? ReadString(JsonElement element, string property)
    {
        var value = ReadProperty(element, property);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static JsonElement ReadCurrentHistoryEntry(JsonElement history)
    {
        var currentIndex = ReadProperty(history, "currentIndex");
        var entries = ReadProperty(history, "entries");
        if (currentIndex.ValueKind != JsonValueKind.Number ||
            !currentIndex.TryGetInt32(out var index) ||
            entries.ValueKind != JsonValueKind.Array ||
            index < 0 ||
            index >= entries.GetArrayLength())
        {
            return default;
        }

        return entries[index];
    }

    private static int CountPageTargets(JsonElement targets)
    {
        if (targets.ValueKind != JsonValueKind.Array)
        {
            return -1;
        }

        var count = 0;
        foreach (var target in targets.EnumerateArray())
        {
            if (ReadString(target, "type") == "page")
            {
                count++;
            }
        }

        return count;
    }

    private static int CountSecurityRelevantTargets(JsonElement targets)
    {
        if (targets.ValueKind != JsonValueKind.Array)
        {
            return -1;
        }

        var count = 0;
        foreach (var target in targets.EnumerateArray())
        {
            if (ReadString(target, "type") != "browser")
            {
                count++;
            }
        }

        return count;
    }

    private sealed record BrowserEventCommand(IPage? Page, string? TargetId)
    {
        internal static BrowserEventCommand ForPage(IPage page) => new(page, null);

        internal static BrowserEventCommand ForTarget(string targetId) => new(null, targetId);
    }
}
