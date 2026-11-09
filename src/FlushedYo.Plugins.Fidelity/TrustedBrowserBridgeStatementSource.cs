using FlushedYo.Core.AgentBridge;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Fidelity statement source backed by the in-process trusted browser bridge. Credentials are
/// resolved from <see cref="ISecretStore"/> by key only; no secret value ever enters logs or
/// model-visible output.
/// </summary>
public sealed class TrustedBrowserBridgeStatementSource : IFidelityStatementSource
{
    private readonly TrustedBrowserBridge _bridge;
    private readonly FidelityProviderOptions _options;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<TrustedBrowserBridgeStatementSource> _logger;
    private BrowserSecuritySnapshot? _loginSnapshot;
    private string[] _accountKeys = [];

    /// <summary>Initializes the statement source.</summary>
    public TrustedBrowserBridgeStatementSource(
        TrustedBrowserBridge bridge,
        FidelityProviderOptions options,
        ISecretStore secretStore,
        ILogger<TrustedBrowserBridgeStatementSource> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _options = options;
        _secretStore = secretStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OpenAsync(Uri loginUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loginUrl);
        await _bridge.NavigateAsync(loginUrl, cancellationToken).ConfigureAwait(false);
        _loginSnapshot = await _bridge.GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false);
        _accountKeys = [];
    }

    /// <inheritdoc />
    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _loginSnapshot ?? await _bridge.GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false);

        // Fill the username (no submit), then submit the password and wait for manual MFA.
        await _bridge.EnterCredentialAsync(
            _options.UsernameKey,
            _options.UsernameSelector,
            snapshot,
            cancellationToken).ConfigureAwait(false);

        snapshot = await _bridge.GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false);
        await _bridge.SubmitCredentialAsync(
            new SubmitCredentialArgs(_options.PasswordKey, _options.PasswordSelector, snapshot),
            cancellationToken).ConfigureAwait(false);

        await _bridge.WaitForManualMfaAsync(
            _options.PostLoginOrigin,
            _options.MfaTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_accountKeys);
    }

    /// <inheritdoc />
    public async Task<StatementArtifact> DownloadStatementAsync(
        string accountKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var snapshot = await _bridge.GetSecuritySnapshotAsync(cancellationToken).ConfigureAwait(false);
        var artifact = await _bridge.DownloadStatementAsync(
            _options.DownloadTriggerSelector,
            snapshot,
            new DownloadStatementOptions(_options.ArtifactDirectory, _options.MaxArtifactBytes, _options.DownloadTimeout),
            cancellationToken).ConfigureAwait(false);
        return new StatementArtifact(artifact.Path, accountKey, snapshot);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _bridge.DisposeAsync();
}
