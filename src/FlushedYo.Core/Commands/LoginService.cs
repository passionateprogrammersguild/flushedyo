using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Checks that each plugin's configured credential key resolves to a login credential. The retrieved
/// credential is discarded immediately and never logged or included in the report.
/// </summary>
/// <remarks>
/// Store failures (for example a locked vault or duplicate key) are not caught: they propagate to the
/// top-level handler.
/// </remarks>
public sealed class LoginService(ISecretStore secretStore)
{
    private const string CredentialName = "credential";

    /// <summary>Checks the credential key of each plugin.</summary>
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

            var check = await CheckAsync(plugin.Configuration.CredentialKey, cancellationToken).ConfigureAwait(false);
            results.Add(new PluginLoginResult(plugin.Name, [check]));
        }

        return new LoginReport(results);
    }

    private async Task<CredentialCheck> CheckAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new CredentialCheck(CredentialName, null, CredentialStatus.NotConfigured);
        }

        var credential = await secretStore.GetCredentialAsync(key, cancellationToken).ConfigureAwait(false);
        var status = credential is null ? CredentialStatus.Missing : CredentialStatus.Ok;
        return new CredentialCheck(CredentialName, key, status);
    }
}
