using System.Text.Json;
using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

// Structured security state: CDP reads, event subscriptions, fault recording, and JSON helpers.
// No page prose, DOM text, or accessibility text is read here.
internal sealed partial class PlaywrightBrowserSessionDriver
{
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
            TargetId = ReadString(ReadProperty(target, "targetInfo"), "targetId"),
            TargetType = ReadString(ReadProperty(target, "targetInfo"), "type"),
            TargetUrl = ReadString(ReadProperty(target, "targetInfo"), "url"),
            TopLevelFrameId = ReadString(frame, "id"),
            LoaderId = ReadString(frame, "loaderId"),
            FrameUrl = ReadString(frame, "url"),
            SecurityOrigin = ReadString(frame, "securityOrigin"),
            IsTopLevelFrame = frame.ValueKind == JsonValueKind.Object && !frame.TryGetProperty("parentId", out _),
            NavigationHistoryUrl = ReadString(historyEntry, "url"),
            PageTargetCount = CountTargets(targetInfos, t => t == "page"),
            TotalTargetCount = CountTargets(targetInfos, t => t != "browser"),
            ContextPageCount = context.Pages.Count,
            IsOriginalPage = context.Pages.Count == 1 && ReferenceEquals(context.Pages[0], page),
            BrowserConnected = RequiredBrowser().IsConnected,
            PageClosed = page.IsClosed,
        };
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

        var targetId = ReadString(ReadProperty(payload, "targetInfo"), "targetId");
        var targetType = ReadString(ReadProperty(payload, "targetInfo"), "type");
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

    private async Task<JsonElement> SendAsync(
        string method,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken) =>
        await SendAsync(RequiredCdp(), method, parameters, cancellationToken).ConfigureAwait(false);

    internal async Task<JsonElement> SendAsync(
        ICDPSession session,
        string method,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var result = await AwaitBrowserTaskAsync(
            session.SendAsync(method, parameters),
            cancellationToken).ConfigureAwait(false);
        return result ?? throw new BridgeSnapshotException("The browser returned no structured security metadata.");
    }

    private void Fault(string reason)
    {
        lock (_faultLock)
        {
            if (Volatile.Read(ref _faulted) == 0)
            {
                _faultReason = reason;
                Volatile.Write(ref _faulted, 1);
            }
        }

        ChangeSecurityState();
    }

    private void ChangeSecurityState() => Interlocked.Increment(ref _securityVersion);

    private BrowserSecuritySnapshotReader RequiredSnapshotReader() =>
        _snapshotReader ?? throw new BridgeOperationException("The browser snapshot reader is not started.");

    private static JsonElement ReadProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value
            : default;

    private static string? ReadString(JsonElement element, string property)
    {
        var value = ReadProperty(element, property);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static JsonElement ReadCurrentHistoryEntry(JsonElement history)
    {
        var currentIndex = ReadProperty(history, "currentIndex");
        var entries = ReadProperty(history, "entries");
        return TryReadCurrentEntryIndex(currentIndex, entries, out var index)
            ? entries[index]
            : default;
    }

    private static bool TryReadCurrentEntryIndex(JsonElement currentIndex, JsonElement entries, out int index)
    {
        index = -1;
        if (currentIndex.ValueKind != JsonValueKind.Number || !currentIndex.TryGetInt32(out var candidate))
        {
            return false;
        }

        if (entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        index = candidate;
        return candidate >= 0 && candidate < entries.GetArrayLength();
    }

    private static int CountTargets(JsonElement targets, Func<string?, bool> match)
    {
        if (targets.ValueKind != JsonValueKind.Array)
        {
            return -1;
        }

        var count = 0;
        foreach (var target in targets.EnumerateArray())
        {
            if (match(ReadString(target, "type")))
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
