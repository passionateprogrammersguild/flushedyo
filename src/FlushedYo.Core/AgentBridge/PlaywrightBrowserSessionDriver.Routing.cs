using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

// Navigation entry points: the provider-visible navigate call and Playwright's route callback.
// Both defer every policy decision to NavigationRequestClassifier.
internal sealed partial class PlaywrightBrowserSessionDriver
{
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

    private async Task RouteAsync(IRoute route)
    {
        ChangeSecurityState();
        try
        {
            var request = route.Request;
            var decision = NavigationRequestClassifier.Classify(
                new NavigationRequest(
                    request.ResourceType,
                    request.Url,
                    _submitOriginPin,
                    Volatile.Read(ref _freezeDocumentNavigations) != 0),
                _policy);
            if (!decision.IsAbort)
            {
                await route.ContinueAsync().ConfigureAwait(false);
                return;
            }

            Fault(decision.FaultCategory);
            await route.AbortAsync("BlockedByClient").ConfigureAwait(false);
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
}
