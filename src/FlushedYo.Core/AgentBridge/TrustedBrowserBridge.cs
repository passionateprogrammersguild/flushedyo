using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// The only provider-facing browser surface. It retrieves credentials directly from
/// <see cref="ISecretStore"/> and offers only typed navigation, credential entry, and manual-MFA
/// operations. Every operation is session-exclusive and every security or operational fault closes
/// the isolated browser.
/// </summary>
/// <remarks>
/// Trust assumptions: the operating system, same-user processes, Chromium, and an explicitly allowed
/// site are trusted. A same-user process can inspect managed memory; an allowed site can read credentials
/// entered into it; managed strings cannot be reliably zeroed. Model-visible disclosure is limited to
/// values explicitly returned by provider operations. No protection is claimed beyond those controls.
/// </remarks>
public sealed partial class TrustedBrowserBridge : IAsyncDisposable
{
    private static readonly TimeSpan MfaPollInterval = TimeSpan.FromMilliseconds(50);
    private readonly AgentBridgeOptions _options;
    private readonly ProviderBrowserPolicy _policy;
    private readonly IBrowserSessionDriver _driver;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<TrustedBrowserBridge> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _started;
    private int _faulted;
    private int _disposed;

    /// <summary>Initializes a lazy, isolated Playwright browser bridge.</summary>
    public TrustedBrowserBridge(
        AgentBridgeOptions options,
        ISecretStore secretStore,
        ILogger<TrustedBrowserBridge>? logger = null,
        TimeProvider? timeProvider = null)
        : this(options, secretStore, logger, timeProvider, driver: null)
    {
    }

    internal TrustedBrowserBridge(
        IBrowserSessionDriver driver,
        AgentBridgeOptions options,
        ISecretStore secretStore,
        ILogger<TrustedBrowserBridge>? logger = null,
        TimeProvider? timeProvider = null)
        : this(options, secretStore, logger, timeProvider, driver)
    {
    }

    private TrustedBrowserBridge(
        AgentBridgeOptions options,
        ISecretStore secretStore,
        ILogger<TrustedBrowserBridge>? logger,
        TimeProvider? timeProvider,
        IBrowserSessionDriver? driver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secretStore);
        options.Validate();
        _options = options;
        _policy = new ProviderBrowserPolicy(options.AllowedOrigins);
        _secretStore = secretStore;
        _logger = logger ?? NullLogger<TrustedBrowserBridge>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _driver = driver ?? new PlaywrightBrowserSessionDriver(_policy, options, _timeProvider);
    }

    /// <summary>Navigates to an allowed absolute URL and verifies its committed structured CDP identity.</summary>
    public async Task NavigateAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        await RunOperationAsync(async token =>
        {
            if (!_policy.IsAllowed(url))
            {
                throw new BridgePolicyException("The browser destination origin is not allowed.");
            }

            await EnsureStartedAsync(token).ConfigureAwait(false);
            await _driver.NavigateAsync(url, token).ConfigureAwait(false);
            _ = await _driver.GetSecuritySnapshotAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the current verified structured CDP snapshot, or fails closed.</summary>
    public Task<BrowserSecuritySnapshot> GetSecuritySnapshotAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(_driver.GetSecuritySnapshotAsync, cancellationToken);

    /// <summary>
    /// Retrieves a website credential internally and fills exactly one matching control without submitting.
    /// The expected snapshot is rechecked around every DOM step and document navigation is frozen.
    /// </summary>
    public Task EnterCredentialAsync(
        string secretKey,
        string cssSelector,
        BrowserSecuritySnapshot expectedSnapshot,
        CancellationToken cancellationToken = default) =>
        EnterCredentialCoreAsync(
            CredentialRequest.Create(secretKey, cssSelector, expectedSnapshot, submit: false),
            cancellationToken);

    /// <summary>
    /// Retrieves a website credential internally, fills exactly one matching control, and submits its form.
    /// A target, document, origin, or security-version change through submission fails closed.
    /// </summary>
    public Task SubmitCredentialAsync(
        string secretKey,
        string cssSelector,
        BrowserSecuritySnapshot expectedSnapshot,
        CancellationToken cancellationToken = default) =>
        EnterCredentialCoreAsync(
            CredentialRequest.Create(secretKey, cssSelector, expectedSnapshot, submit: true),
            cancellationToken);

    /// <summary>
    /// Waits without code collection or automatic MFA until the structured top-level origin reaches an
    /// allowed post-login origin. The timeout must be positive and no greater than the configured maximum.
    /// </summary>
    public Task<BrowserSecuritySnapshot> WaitForManualMfaAsync(
        Uri expectedPostLoginOriginOrUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedPostLoginOriginOrUrl);
        if (timeout <= TimeSpan.Zero || timeout > _options.MaxMfaTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The manual MFA timeout is outside the configured bounds.");
        }

        return RunOperationAsync(async token =>
        {
            var expectedOrigin = _policy.GetOrigin(expectedPostLoginOriginOrUrl);
            var deadline = _timeProvider.GetUtcNow() + timeout;
            while (true)
            {
                var snapshot = await _driver.GetSecuritySnapshotAsync(token).ConfigureAwait(false);
                if (string.Equals(snapshot.Origin, expectedOrigin, StringComparison.Ordinal))
                {
                    return snapshot;
                }

                var remaining = deadline - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    throw new BridgeMfaException("The bounded manual MFA pause timed out.");
                }

                await Task.Delay(
                    remaining < MfaPollInterval ? remaining : MfaPollInterval,
                    _timeProvider,
                    token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Cancels in-flight work, acquires the operation gate, and fully awaits bounded browser teardown.
    /// A teardown timeout is reported without browser diagnostics.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        using var teardown = new CancellationTokenSource(_options.TeardownTimeout, _timeProvider);
        var gateTaken = false;
        try
        {
            await _operationGate.WaitAsync(teardown.Token).ConfigureAwait(false);
            gateTaken = true;
            await _driver.CloseAsync(teardown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (teardown.IsCancellationRequested)
        {
            _driver.ForceClose();
            throw new BridgeOperationException("The bounded browser teardown timed out.");
        }
        catch (BridgeException)
        {
            _driver.ForceClose();
            throw;
        }
        catch (Exception)
        {
            _driver.ForceClose();
            throw new BridgeOperationException("The isolated browser teardown failed.");
        }
        finally
        {
            if (gateTaken)
            {
                _operationGate.Release();
            }

            _lifetime.Dispose();
        }
    }

    private Task<bool> EnterCredentialCoreAsync(
        CredentialRequest request,
        CancellationToken cancellationToken)
    {
        var expectation = new PostSubmitExpectation(request.ExpectedSnapshot);
        return RunOperationAsync(async token =>
        {
            await _driver.SetDocumentNavigationFrozenAsync(true, token).ConfigureAwait(false);
            try
            {
                await VerifyExpectedSnapshotAsync(request.ExpectedSnapshot, token).ConfigureAwait(false);
                string? secret = await _secretStore.GetSecretAsync(request.SecretKey, token).ConfigureAwait(false);
                if (secret is null)
                {
                    throw new BridgeOperationException("The requested website credential is unavailable.");
                }

                try
                {
                    await _driver.EnterCredentialAsync(
                        request.CssSelector,
                        secret,
                        request.Submit,
                        token => VerifyExpectedSnapshotAsync(request.ExpectedSnapshot, token),
                        token => VerifyPostSubmitSnapshotAsync(expectation, token),
                        token).ConfigureAwait(false);
                }
                finally
                {
                    secret = null;
                }

                return true;
            }
            finally
            {
                await _driver.SetDocumentNavigationFrozenAsync(false, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    private async Task VerifyExpectedSnapshotAsync(
        BrowserSecuritySnapshot expectedSnapshot,
        CancellationToken cancellationToken)
    {
        var actual = await _driver.GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (actual != expectedSnapshot)
        {
            throw new BridgeSnapshotException("The browser security snapshot changed before credential completion.");
        }
    }

    private async Task VerifyPostSubmitSnapshotAsync(
        PostSubmitExpectation expectation,
        CancellationToken cancellationToken)
    {
        var actual = await _driver.GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!SatisfiesPostSubmitIdentity(expectation, actual))
        {
            throw new BridgeSnapshotException("The browser target or origin changed unexpectedly after submission.");
        }

        // A submission is only reported once a new document committed. A page that swallows the
        // keypress, or a same-document update, is not evidence that the form was submitted.
        if (!expectation.HasNewDocument(actual))
        {
            throw new BridgeOperationException("No document navigation was observed after submission.");
        }
    }

    private bool SatisfiesPostSubmitIdentity(PostSubmitExpectation expectation, BrowserSecuritySnapshot actual)
    {
        if (!expectation.HasSameIdentity(actual))
        {
            return false;
        }

        if (!expectation.HasSameOrigin(actual))
        {
            return false;
        }

        return IsAllowedOrigin(actual.Origin);
    }

    private bool IsAllowedOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var allowed) && _policy.IsAllowed(allowed);

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            try
            {
                await _driver.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref _started, 0);
                throw;
            }
        }
    }

    private async Task<T> RunOperationAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _faulted) != 0 || _driver.IsFaulted)
            {
                throw new BridgeOperationException("The browser session is faulted and cannot be reused.");
            }

            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await FaultAndCloseAsync().ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (BridgeException)
        {
            await FaultAndCloseAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await FaultAndCloseAsync().ConfigureAwait(false);
            throw new BridgeOperationException("The browser operation failed safely.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task FaultAndCloseAsync()
    {
        Interlocked.Exchange(ref _faulted, 1);
        try
        {
            using var teardown = new CancellationTokenSource(_options.TeardownTimeout, _timeProvider);
            await _driver.CloseAsync(teardown.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _driver.ForceClose();
            LogFaultTeardownFailed();
        }
    }

    [LoggerMessage(EventId = 4800, Level = LogLevel.Error, Message = "The faulted browser session did not tear down cleanly.")]
    private partial void LogFaultTeardownFailed();
}
