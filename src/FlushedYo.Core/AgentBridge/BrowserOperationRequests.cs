namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// One provider credential request: the secret key to resolve internally, the single control to fill,
/// and the verified snapshot the operation must still match. The value itself never appears here.
/// </summary>
internal readonly record struct CredentialRequest(
    string SecretKey,
    string CssSelector,
    BrowserSecuritySnapshot ExpectedSnapshot,
    bool Submit)
{
    /// <summary>Validates the caller's arguments and wraps them, without ever holding the secret value.</summary>
    internal static CredentialRequest Create(
        string secretKey,
        string cssSelector,
        BrowserSecuritySnapshot expectedSnapshot,
        bool submit)
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
        return new CredentialRequest(secretKey, cssSelector, expectedSnapshot, submit);
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
