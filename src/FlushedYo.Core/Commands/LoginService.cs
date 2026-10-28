using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Checks that each plugin's configured credential keys exist in the secret store. It asks only whether a
/// key exists and never keeps, logs, or returns a secret value.
/// </summary>
/// <remarks>
/// Both real secret stores implement <see cref="ISecretStore.SecretExistsAsync"/> by fetching the value and
/// testing it for null, so the value passes through memory inside the store; this service never sees it.
/// Store failures (for example a locked vault) are not caught: they propagate to the top-level handler.
/// </remarks>
public sealed class LoginService(ISecretStore secretStore)
{
    private const string UsernameCredential = "username";
    private const string PasswordCredential = "password";

    /// <summary>Checks the username and password keys of each plugin.</summary>
    /// <param name="plugins">The plugins to check, in the order to report them.</param>
    /// <param name="cancellationToken">Token that cancels the check between plugins.</param>
    /// <returns>One result per plugin.</returns>
    public async Task<LoginReport> CheckAsync(IReadOnlyList<PluginDescriptor> plugins, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var results = new List<PluginLoginResult>(plugins.Count);
        foreach (var plugin in plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var keys = plugin.Configuration.CredentialKeys;
            results.Add(new PluginLoginResult(
                plugin.Name,
                [
                    await CheckAsync(UsernameCredential, keys.Username, cancellationToken).ConfigureAwait(false),
                    await CheckAsync(PasswordCredential, keys.Password, cancellationToken).ConfigureAwait(false),
                ]));
        }

        return new LoginReport(results);
    }

    private async Task<CredentialCheck> CheckAsync(string credential, string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new CredentialCheck(credential, null, CredentialStatus.NotConfigured);
        }

        var exists = await secretStore.SecretExistsAsync(key, cancellationToken).ConfigureAwait(false);
        return new CredentialCheck(credential, key, exists ? CredentialStatus.Ok : CredentialStatus.Missing);
    }
}
