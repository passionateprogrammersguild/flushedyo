using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

// Credential entry step machine: resolve one control, focus, fill, and optionally submit, with a
// snapshot re-verification around every DOM step. The secret stays a local of this partial only.
internal sealed partial class PlaywrightBrowserSessionDriver
{
    private async Task RunCredentialStepsAsync(
        CredentialEntryOperation operation,
        string secret,
        CancellationToken cancellationToken)
    {
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        operation.Element = await ResolveUniqueElementAsync(operation, cancellationToken).ConfigureAwait(false);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        await FocusAsync(operation, cancellationToken).ConfigureAwait(false);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        await FillAsync(operation, secret, cancellationToken).ConfigureAwait(false);
        if (operation.Plan.Submit)
        {
            await SubmitAsync(operation, cancellationToken).ConfigureAwait(false);
            await operation.VerifyAfterSubmitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IElementHandle> ResolveUniqueElementAsync(
        CredentialEntryOperation operation,
        CancellationToken cancellationToken)
    {
        operation.Step = "selector resolution";
        var elements = await AwaitBrowserTaskAsync(
            RequiredPage().QuerySelectorAllAsync(operation.Plan.CssSelector),
            cancellationToken).ConfigureAwait(false);
        if (elements.Count != 1)
        {
            throw new BridgeOperationException("The configured credential control is missing or ambiguous.");
        }

        return elements[0];
    }

    private async Task FocusAsync(CredentialEntryOperation operation, CancellationToken cancellationToken)
    {
        operation.Step = "focus";
        await AwaitBrowserTaskAsync(RequiredElement(operation).FocusAsync(), cancellationToken).ConfigureAwait(false);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FillAsync(
        CredentialEntryOperation operation,
        string secret,
        CancellationToken cancellationToken)
    {
        operation.Step = "fill";
        await AwaitBrowserTaskAsync(RequiredElement(operation).FillAsync(secret), cancellationToken).ConfigureAwait(false);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SubmitAsync(CredentialEntryOperation operation, CancellationToken cancellationToken)
    {
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        operation.Step = "submission";
        // Submission is the one document navigation the bridge itself initiates. Disallowed
        // destinations are still aborted by the route policy, and the post-submit check
        // requires the same target and frame at an allowed origin.
        // While the submission is in flight only the verified origin may receive the document
        // request, so a retargeted form action cannot deliver the secret to a second allowed origin.
        _submitOriginPin = (await GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false)).Origin;
        Interlocked.Exchange(ref _freezeDocumentNavigations, 0);
        await AwaitBrowserTaskAsync(RequiredElement(operation).PressAsync("Enter"), cancellationToken).ConfigureAwait(false);
        _submitOriginPin = null;
    }

    private async Task DisposeElementAsync(CredentialEntryOperation operation)
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
            Fault("browser state");
        }
    }

    private static IElementHandle RequiredElement(CredentialEntryOperation operation) =>
        operation.Element ?? throw new BridgeOperationException("The credential control is no longer available.");
}
