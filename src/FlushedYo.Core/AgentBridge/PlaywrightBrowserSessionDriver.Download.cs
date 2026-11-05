using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

// Download step machine: resolve one trigger control, open a deny-by-default download window, pin
// the verified origin, claim exactly one download into a caller-owned directory, enforce the byte
// cap before any copy, and delete every partial artifact on any failure. No file content is read.
internal sealed partial class PlaywrightBrowserSessionDriver
{
    private int _downloadWindowOpen;
    private string? _downloadScratchDirectory;

    public async Task<BrowserArtifact> DownloadAsync(
        DownloadRequest request,
        Func<CancellationToken, Task> verifySecurityState,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var operation = new DownloadOperation(request, verifySecurityState);
        try
        {
            return await RunDownloadStepsAsync(operation, cancellationToken).ConfigureAwait(false);
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
            throw new BridgeOperationException($"The download {operation.Step} failed safely ({_faultReason}).");
        }
        finally
        {
            _submitOriginPin = null;
            await CloseDownloadWindowAsync().ConfigureAwait(false);
            await DisposeDownloadElementAsync(operation).ConfigureAwait(false);
        }
    }

    private async Task<BrowserArtifact> RunDownloadStepsAsync(
        DownloadOperation operation,
        CancellationToken cancellationToken)
    {
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        await OpenDownloadWindowAsync(cancellationToken).ConfigureAwait(false);
        operation.Element = await ResolveUniqueDownloadControlAsync(operation, cancellationToken)
            .ConfigureAwait(false);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);

        var download = await TriggerDownloadAsync(operation, cancellationToken).ConfigureAwait(false);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);

        var artifact = ClaimArtifact(operation, download);
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        operation.Artifact = artifact;
        return artifact;
    }
}
