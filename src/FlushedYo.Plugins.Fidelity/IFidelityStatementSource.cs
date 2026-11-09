using FlushedYo.Core.AgentBridge;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Source of Fidelity statement artifacts: opens a trusted browser session, logs in,
/// enumerates accounts, and downloads per-account statement exports.
/// </summary>
public interface IFidelityStatementSource : IAsyncDisposable
{
    /// <summary>Opens the trusted browser and navigates to the Fidelity login URL.</summary>
    /// <param name="loginUrl">The fully-qualified Fidelity login URL.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task OpenAsync(Uri loginUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enters credentials from the secret store and submits the login form, then waits
    /// for any manual MFA to reach an allowed post-login origin.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task LoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the account keys (masked, stable identifiers) available to the logged-in user.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the statement artifact for one account into the options' artifact directory.
    /// </summary>
    /// <param name="accountKey">The masked account key to download.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The downloaded artifact: path, account key, and verified snapshot.</returns>
    Task<StatementArtifact> DownloadStatementAsync(string accountKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// A downloaded Fidelity statement artifact: its path inside the caller-owned directory,
/// the account key it belongs to, and the verified browser snapshot that produced it.
/// </summary>
public sealed record StatementArtifact
{
    internal StatementArtifact(string path, string accountKey, BrowserSecuritySnapshot snapshot)
    {
        Path = path;
        AccountKey = accountKey;
        Snapshot = snapshot;
    }

    /// <summary>Gets the absolute path of the downloaded artifact.</summary>
    public string Path { get; }

    /// <summary>Gets the masked account key the artifact belongs to.</summary>
    public string AccountKey { get; }

    /// <summary>Gets the verified browser snapshot the artifact was downloaded under.</summary>
    public BrowserSecuritySnapshot Snapshot { get; }
}
