namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// One provider credential request: the validated provider arguments plus whether the control's form
/// is submitted. The secret value itself never appears here.
/// </summary>
internal readonly record struct CredentialRequest(SubmitCredentialArgs Args, bool Submit)
{
    internal string SecretKey => Args.SecretKey;

    internal string CssSelector => Args.CssSelector;

    internal BrowserSecuritySnapshot ExpectedSnapshot => Args.ExpectedSnapshot;

    /// <summary>Wraps a fill-only request for the validated provider arguments.</summary>
    internal static CredentialRequest Entry(SubmitCredentialArgs args) => new(args, Submit: false);

    /// <summary>Wraps a submit request for the validated provider arguments.</summary>
    internal static CredentialRequest Submission(SubmitCredentialArgs args) => new(args, Submit: true);
}

/// <summary>
/// One provider credential submission: the secret key to resolve internally, the single control to
/// fill, and the verified snapshot the operation must still match. The value itself never appears
/// here; submission is implicit (Submit is always true).
/// </summary>
public readonly record struct SubmitCredentialArgs(
    string SecretKey,
    string CssSelector,
    BrowserSecuritySnapshot ExpectedSnapshot)
{
    /// <summary>Validates the caller's arguments and wraps them, without ever holding the secret value.</summary>
    internal static SubmitCredentialArgs Create(
        string secretKey,
        string cssSelector,
        BrowserSecuritySnapshot expectedSnapshot)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new ArgumentException("A website secret key is required.", nameof(secretKey));
        }

        if (string.IsNullOrWhiteSpace(cssSelector))
        {
            throw new ArgumentException("A credential CSS selector is required.", nameof(cssSelector));
        }

        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        return new SubmitCredentialArgs(secretKey, cssSelector, expectedSnapshot);
    }
}

/// <summary>
/// The pre-submit snapshot a completed submission must still match. It carries only structured
/// identity fields, so no page content participates in any check.
/// </summary>
internal readonly record struct PostSubmitExpectation(BrowserSecuritySnapshot Expected)
{
    /// <summary>Gets a value indicating whether the same target still owns the same top-level frame.</summary>
    internal bool HasSameIdentity(BrowserSecuritySnapshot actual)
    {
        if (!string.Equals(actual.TargetId, Expected.TargetId, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(actual.TopLevelFrameId, Expected.TopLevelFrameId, StringComparison.Ordinal);
    }

    /// <summary>Gets a value indicating whether the committed origin is unchanged.</summary>
    internal bool HasSameOrigin(BrowserSecuritySnapshot actual) =>
        string.Equals(actual.Origin, Expected.Origin, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether a new document committed after the submission.</summary>
    internal bool HasNewDocument(BrowserSecuritySnapshot actual) =>
        !string.Equals(actual.LoaderId, Expected.LoaderId, StringComparison.Ordinal);
}
