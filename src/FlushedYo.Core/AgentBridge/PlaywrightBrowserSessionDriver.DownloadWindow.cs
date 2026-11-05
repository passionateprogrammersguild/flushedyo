using System.Text.Json;
using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

// The download window is the only place downloads are enabled, and it exists only while one
// provider download is in flight. The context stays deny-by-default; this window flips Chromium's
// browser-level download behavior to allowAndName for exactly one context, for exactly one
// operation, and reverts to deny on every exit path.
internal sealed partial class PlaywrightBrowserSessionDriver
{
    private const string AllowAndNameDownloadBehavior = "allowAndName";
    private const string DenyDownloadBehavior = "deny";
    private const string DownloadAnnouncedEvent = "Browser.downloadWillBegin";
    private const string DownloadProgressEvent = "Browser.downloadProgress";

    private string? _browserContextId;
    private ICDPSession? _browserCdp;
    private DownloadCapture? _downloadCapture;

    private async Task OpenDownloadWindowAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _downloadWindowOpen, 1) != 0)
        {
            throw new BridgeOperationException("A download window is already open.");
        }

        try
        {
            _downloadCapture = new DownloadCapture();
            _browserCdp = await AwaitBrowserTaskAsync(
                RequiredBrowser().NewBrowserCDPSessionAsync(),
                cancellationToken).ConfigureAwait(false);
            SubscribeToDownloadEvents(_browserCdp, _downloadCapture);
            await SetDownloadBehaviorAsync(
                await ResolveBrowserContextIdAsync(cancellationToken).ConfigureAwait(false),
                AllowAndNameDownloadBehavior,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _downloadWindowOpen, 0);
            _downloadCapture = null;
            await DetachBrowserCdpAsync().ConfigureAwait(false);
            throw new BridgeOperationException("The download window could not be opened.");
        }
    }

    // Chromium reports downloads on the browser-level channel only. The subscription lives exactly as
    // long as the window; the payload fields read here are the browser's own identifiers, never content.
    private static void SubscribeToDownloadEvents(ICDPSession session, DownloadCapture capture)
    {
        session.Event(DownloadAnnouncedEvent).OnEvent += (_, data) => OnDownloadAnnounced(capture, data);
        session.Event(DownloadProgressEvent).OnEvent += (_, data) => OnDownloadProgress(capture, data);
    }

    private static void OnDownloadAnnounced(DownloadCapture capture, JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } payload)
        {
            return;
        }

        var guid = ReadString(payload, "guid");
        if (string.IsNullOrEmpty(guid))
        {
            capture.Progress(guid, state: null);
            return;
        }

        capture.Announce(new DownloadBegin(
            guid,
            ReadString(payload, "suggestedFilename"),
            ReadString(payload, "url")));
    }

    // Chromium reports inProgress, then completed or canceled; only the terminal states settle the
    // capture, so a slow artifact is never mistaken for a refused one.
    private static void OnDownloadProgress(DownloadCapture capture, JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } payload)
        {
            return;
        }

        capture.Progress(ReadString(payload, "guid"), ReadString(payload, "state"));
    }

    private async Task CloseDownloadWindowAsync()
    {
        if (!TryClaimDownloadWindow())
        {
            return;
        }

        try
        {
            if (ShouldRestoreDenyDownloadBehavior())
            {
                await SetDownloadBehaviorAsync(_browserContextId!, DenyDownloadBehavior, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            Fault("browser state");
        }
        finally
        {
            _downloadCapture = null;
            await DetachBrowserCdpAsync().ConfigureAwait(false);
            ClearDownloadScratchDirectory();
        }
    }

    private bool TryClaimDownloadWindow() =>
        Interlocked.Exchange(ref _downloadWindowOpen, 0) != 0;

    private bool ShouldRestoreDenyDownloadBehavior() =>
        _context is not null &&
        Volatile.Read(ref _closed) == 0 &&
        _browserContextId is not null;

    private async Task DetachBrowserCdpAsync()
    {
        var session = _browserCdp;
        _browserCdp = null;
        if (session is null)
        {
            return;
        }

        try
        {
            await AwaitBrowserTaskAsync(session.DetachAsync(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A detached or closing browser session has nothing left to detach.
        }
    }

    // The window is scoped to the session's single context, so the context id must be unambiguous and
    // must never change. A second context, or a changed id, fails closed instead of widening the window.
    private async Task<string> ResolveBrowserContextIdAsync(CancellationToken cancellationToken)
    {
        var contexts = await SendAsync(
            RequiredBrowserCdp(),
            "Target.getBrowserContexts",
            parameters: null,
            cancellationToken).ConfigureAwait(false);
        var ids = ReadProperty(contexts, "browserContextIds");
        var contextId = ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() == 1
            ? ids[0].GetString()
            : null;
        if (string.IsNullOrEmpty(contextId))
        {
            throw new BridgeSnapshotException("The browser download window target is ambiguous.");
        }

        if (_browserContextId is not null && !string.Equals(_browserContextId, contextId, StringComparison.Ordinal))
        {
            throw new BridgeSnapshotException("The browser download window target changed.");
        }

        _browserContextId = contextId;
        return contextId;
    }

    private async Task SetDownloadBehaviorAsync(string browserContextId, string behavior, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["behavior"] = behavior,
            ["browserContextId"] = browserContextId,
            ["eventsEnabled"] = true,
        };
        if (behavior != DenyDownloadBehavior)
        {
            parameters["downloadPath"] = RequiredDownloadScratchDirectory();
        }

        await SendAsync(RequiredBrowserCdp(), "Browser.setDownloadBehavior", parameters, cancellationToken)
            .ConfigureAwait(false);
    }

    private ICDPSession RequiredBrowserCdp() =>
        _browserCdp ?? throw new BridgeOperationException("The browser download channel is not started.");

    private async Task<IElementHandle> ResolveUniqueDownloadControlAsync(
        DownloadOperation operation,
        CancellationToken cancellationToken)
    {
        operation.Step = DownloadOperation.SelectorResolutionStep;
        var elements = await AwaitBrowserTaskAsync(
            RequiredPage().QuerySelectorAllAsync(operation.Request.CssSelector),
            cancellationToken).ConfigureAwait(false);
        if (elements.Count != 1)
        {
            throw new BridgeOperationException("The configured download control is missing or ambiguous.");
        }

        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        return elements[0];
    }

    private static async Task DisposeDownloadElementAsync(DownloadOperation operation)
    {
        if (operation.Element is null)
        {
            return;
        }

        try
        {
            await operation.Element.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The session is already faulted by the failing step; browser diagnostics never escape.
        }
    }

    private string RequiredDownloadScratchDirectory() =>
        _downloadScratchDirectory ??= Directory.CreateTempSubdirectory("flushedyo-download-").FullName;

    private void ClearDownloadScratchDirectory()
    {
        var directory = _downloadScratchDirectory;
        _downloadScratchDirectory = null;
        if (directory is not null)
        {
            ArtifactCopy.DeleteDirectory(directory);
        }
    }
}
