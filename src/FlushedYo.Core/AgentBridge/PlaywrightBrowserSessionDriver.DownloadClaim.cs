namespace FlushedYo.Core.AgentBridge;

// Trigger and claim: open the deny-by-default download window, pin the verified origin, wait for the
// single announced download, then move the browser-named artifact from the private scratch directory
// into the caller-owned directory under a hard byte cap. No file content is ever read or copied into
// memory beyond the cap-enforcing file move.
internal sealed partial class PlaywrightBrowserSessionDriver
{
    private async Task<PendingDownload> TriggerDownloadAsync(
        DownloadOperation operation,
        CancellationToken cancellationToken)
    {
        await operation.VerifySecurityStateAsync(cancellationToken).ConfigureAwait(false);
        operation.Step = DownloadOperation.TriggerStep;
        var capture = _downloadCapture
            ?? throw new BridgeOperationException("The download window is not open.");
        _submitOriginPin = operation.Request.ExpectedSnapshot.Origin;
        try
        {
            await AwaitBrowserTaskAsync(
                operation.Element!.ClickAsync(),
                operation.Request.Options.Timeout,
                cancellationToken).ConfigureAwait(false);
            var signal = await AwaitBrowserTaskAsync(
                capture.WaitAsync(operation.Request.Options.Timeout, cancellationToken),
                operation.Request.Options.Timeout,
                cancellationToken).ConfigureAwait(false);
            return new PendingDownload(
                Path.Combine(RequiredDownloadScratchDirectory(), signal.Begin.Guid),
                signal.Begin);
        }
        finally
        {
            _submitOriginPin = null;
        }
    }

    private static BrowserArtifact ClaimArtifact(DownloadOperation operation, PendingDownload download)
    {
        operation.Step = DownloadOperation.ClaimStep;
        var request = operation.Request;
        Directory.CreateDirectory(request.Options.DestinationDirectory);
        var destination = DownloadArtifactPolicy.ResolvePath(
            request.Options.DestinationDirectory,
            download.Begin.SuggestedFileName);
        operation.ArtifactPath = destination;
        try
        {
            DownloadArtifactPolicy.EnsureWithinCap(new FileInfo(download.ScratchPath).Length, request.Options.MaxBytes);
            File.Move(download.ScratchPath, destination, overwrite: false);
        }
        catch (Exception)
        {
            ArtifactCopy.Delete(destination);
            throw;
        }

        return VerifyArtifactSize(operation);
    }

    private static BrowserArtifact VerifyArtifactSize(DownloadOperation operation)
    {
        operation.Step = DownloadOperation.SizeVerificationStep;
        var path = operation.ArtifactPath!;
        long size;
        try
        {
            size = new FileInfo(path).Length;
            DownloadArtifactPolicy.EnsureWithinCap(size, operation.Request.Options.MaxBytes);
        }
        catch (Exception)
        {
            ArtifactCopy.Delete(path);
            throw;
        }

        return new BrowserArtifact(path, size, operation.Request.ExpectedSnapshot.Origin);
    }

    /// <summary>One announced download: the browser's own scratch path and the value-free facts.</summary>
    internal readonly record struct PendingDownload(string ScratchPath, DownloadBegin Begin);
}
