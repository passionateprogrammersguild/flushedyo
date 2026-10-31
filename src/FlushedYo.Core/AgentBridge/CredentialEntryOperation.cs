using Microsoft.Playwright;

namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// One credential entry: the caller-visible selector, the submit flag, and the two snapshot verifiers.
/// The secret is deliberately absent so the plan never retains a value beyond the fill call.
/// </summary>
internal readonly record struct CredentialEntryPlan(
    string CssSelector,
    bool Submit,
    Func<CancellationToken, Task> VerifySecurityState,
    Func<CancellationToken, Task> VerifyAfterSubmit);

/// <summary>
/// Mutable state of one in-flight credential entry. It owns the resolved control and the value-free
/// operation step reported if a non-bridge failure unwinds the step machine.
/// </summary>
internal sealed class CredentialEntryOperation(CredentialEntryPlan plan)
{
    internal const string SecurityVerificationStep = "security verification";
    internal const string PostSubmitStep = "post-submit verification";

    internal CredentialEntryPlan Plan { get; } = plan;

    internal IElementHandle? Element { get; set; }

    internal string Step { get; set; } = SecurityVerificationStep;

    internal Task VerifySecurityStateAsync(CancellationToken cancellationToken)
    {
        Step = SecurityVerificationStep;
        return Plan.VerifySecurityState(cancellationToken);
    }

    internal Task VerifyAfterSubmitAsync(CancellationToken cancellationToken)
    {
        Step = PostSubmitStep;
        return Plan.VerifyAfterSubmit(cancellationToken);
    }
}
