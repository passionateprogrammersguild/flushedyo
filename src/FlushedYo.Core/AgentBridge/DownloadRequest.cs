namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// One provider download request: the single control to trigger, the verified snapshot the operation
/// must still match, and the byte-cap/bounded-wait options for the claim.
/// </summary>
internal readonly record struct DownloadRequest(
    string CssSelector,
    BrowserSecuritySnapshot ExpectedSnapshot,
    DownloadOptions Options)
{
    /// <summary>Validates the caller's arguments and wraps them without holding any file content.</summary>
    internal static DownloadRequest Create(
        string cssSelector,
        BrowserSecuritySnapshot expectedSnapshot,
        DownloadOptions options)
    {
        if (string.IsNullOrWhiteSpace(cssSelector))
        {
            throw new ArgumentException("A download-trigger CSS selector is required.", nameof(cssSelector));
        }

        ArgumentNullException.ThrowIfNull(expectedSnapshot);

        if (options.MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The download size cap must be positive.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The download wait must be positive.");
        }

        return new DownloadRequest(cssSelector, expectedSnapshot, options);
    }
}
