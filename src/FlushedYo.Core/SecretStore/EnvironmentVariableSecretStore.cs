namespace FlushedYo.Core.SecretStore;

/// <summary>
/// Development/test secret store that resolves keys from environment variables.
/// Key "fidelity/username" resolves to the FLUSHEDYO_SECRET_FIDELITY_USERNAME variable.
/// </summary>
public sealed class EnvironmentVariableSecretStore : ISecretStore
{
    public const string Prefix = "FLUSHEDYO_SECRET_";

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(Environment.GetEnvironmentVariable(ToVariableName(key)));
    }

    public Task<Credential?> GetCredentialAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var variableName = ToVariableName(key);
        var username = Environment.GetEnvironmentVariable(variableName + "_USERNAME");
        var password = Environment.GetEnvironmentVariable(variableName + "_PASSWORD");
        Credential? credential = username is null || password is null ? null : new Credential(username, password);
        return Task.FromResult(credential);
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
        => await GetSecretAsync(key, cancellationToken) is not null;

    internal static string ToVariableName(string key)
        => Prefix + key.Trim().Replace('/', '_').Replace('-', '_').ToUpperInvariant();
}
