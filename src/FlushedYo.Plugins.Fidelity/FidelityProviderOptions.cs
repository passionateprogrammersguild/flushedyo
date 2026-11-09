namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Configuration for the Fidelity provider plugin: where statement artifacts land, how large
/// they may grow, how long downloads and logins may wait, and how far back downloads reach.
/// </summary>
public sealed class FidelityProviderOptions
{
    /// <summary>Directory statement artifacts are downloaded into (must be fully-qualified).</summary>
    public string ArtifactDirectory { get; set; } = string.Empty;

    /// <summary>Maximum bytes a single statement artifact may contain.</summary>
    public long MaxArtifactBytes { get; set; } = 50_000_000;

    /// <summary>Maximum time a single statement download may wait.</summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum time a manual MFA pause may last.</summary>
    public TimeSpan MfaTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Number of days to look back when downloading statements.</summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>
    /// Masked, stable account keys to sync. The typed bridge exposes no page-text operation, so the
    /// portal cannot be enumerated live; keys are supplied here and must be the masked values the CSV
    /// adapter derives from each row's account number.
    /// </summary>
    public List<string> AccountKeys { get; set; } = [];

    /// <summary>Secret-store key for the Fidelity username (keys only, never values).</summary>
    public string UsernameKey { get; set; } = "fidelity/username";

    /// <summary>Secret-store key for the Fidelity password (keys only, never values).</summary>
    public string PasswordKey { get; set; } = "fidelity/password";

    /// <summary>CSS selector for the username input on the login form.</summary>
    public string UsernameSelector { get; set; } = "#userId";

    /// <summary>CSS selector for the password input on the login form.</summary>
    public string PasswordSelector { get; set; } = "#password";

    /// <summary>CSS selector that triggers the account statement export/download.</summary>
    public string DownloadTriggerSelector { get; set; } = "button[aria-label='Download']";

    /// <summary>Fully-qualified URL of the Fidelity login page.</summary>
    public Uri LoginUrl { get; set; } = new("https://login.fidelity.com/");

    /// <summary>Expected post-login origin after manual MFA completes.</summary>
    public Uri PostLoginOrigin { get; set; } = new("https://www.fidelity.com/");
}
