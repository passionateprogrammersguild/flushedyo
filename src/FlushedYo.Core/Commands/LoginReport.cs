namespace FlushedYo.Core.Commands;

/// <summary>Whether a plugin's credential key resolved in the secret store.</summary>
public enum CredentialStatus
{
    /// <summary>The key exists in the secret store.</summary>
    Ok,

    /// <summary>The key is configured but the secret store has no such secret.</summary>
    Missing,

    /// <summary>The plugin's <c>config.json</c> does not name a key for this credential.</summary>
    NotConfigured,
}

/// <summary>The result of checking one credential of one plugin.</summary>
/// <param name="Credential">Which credential: <c>username</c> or <c>password</c>.</param>
/// <param name="Key">
/// The secret-store key <em>name</em> from <c>config.json</c> (for example <c>fidelity/username</c>), never a
/// secret value. <see langword="null"/> when <paramref name="Status"/> is <see cref="CredentialStatus.NotConfigured"/>.
/// </param>
/// <param name="Status">Whether the key resolved.</param>
public sealed record CredentialCheck(string Credential, string? Key, CredentialStatus Status);

/// <summary>The credential checks for one plugin.</summary>
/// <param name="Plugin">Plugin name.</param>
/// <param name="Credentials">The username check, then the password check.</param>
public sealed record PluginLoginResult(string Plugin, IReadOnlyList<CredentialCheck> Credentials);

/// <summary>The outcome of <c>login</c> across the selected plugins.</summary>
/// <param name="Plugins">One result per plugin, in the order checked.</param>
public sealed record LoginReport(IReadOnlyList<PluginLoginResult> Plugins)
{
    /// <summary>True when any credential was <see cref="CredentialStatus.Missing"/>.</summary>
    public bool HasMissing =>
        Plugins.Any(static plugin => plugin.Credentials.Any(static check => check.Status == CredentialStatus.Missing));
}
