using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Mutable state of one in-flight download. It owns the resolved control, the artifact path once
/// claimed, and the value-free operation step reported if a non-bridge failure unwinds the machine.
/// No file content and no download URL ever reach this type.
/// </summary>
internal sealed class DownloadOperation
{
    internal const string SecurityVerificationStep = "security verification";
    internal const string SelectorResolutionStep = "selector resolution";
    internal const string TriggerStep = "download trigger";
    internal const string ClaimStep = "artifact claim";
    internal const string SizeVerificationStep = "size verification";

    internal DownloadOperation(DownloadRequest request, Func<CancellationToken, Task> verifySecurityState)
    {
        Request = request;
        VerifySecurityState = verifySecurityState;
    }

    internal DownloadRequest Request { get; }

    internal Func<CancellationToken, Task> VerifySecurityState { get; }

    internal IElementHandle? Element { get; set; }

    internal string Step { get; set; } = SecurityVerificationStep;

    /// <summary>Gets or sets the caller-owned artifact path, set before the file is written.</summary>
    internal string? ArtifactPath { get; set; }

    /// <summary>Gets or sets the verified artifact once the size cap has been satisfied.</summary>
    internal BrowserArtifact? Artifact { get; set; }

    internal Task VerifySecurityStateAsync(CancellationToken cancellationToken)
    {
        Step = SecurityVerificationStep;
        return VerifySecurityState(cancellationToken);
    }
}
