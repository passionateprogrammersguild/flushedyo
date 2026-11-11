using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>
/// An <see cref="ISecretStore"/> for command tests. <see cref="GetSecretAsync"/> throws on purpose so
/// <c>login</c> fails loudly if it uses the legacy single-value lookup.
/// </summary>
internal sealed class FakeSecretStore(
    IReadOnlyDictionary<string, string>? secrets = null,
    SecretStoreException? existsFailure = null) : ISecretStore
{
    private readonly IReadOnlyDictionary<string, string> _secrets = secrets ?? new Dictionary<string, string>();
    private readonly List<string> _checkedKeys = [];

    public IReadOnlyList<string> CheckedKeys => _checkedKeys;

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("GetSecretAsync must not be called");

    public Task<Credential?> GetCredentialAsync(string key, CancellationToken cancellationToken = default)
    {
        _checkedKeys.Add(key);
        if (existsFailure is not null)
        {
            throw existsFailure;
        }

        return Task.FromResult<Credential?>(_secrets.TryGetValue(key, out var value) ? new Credential(value, value) : null);
    }

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _checkedKeys.Add(key);
        return existsFailure is not null
            ? throw existsFailure
            : Task.FromResult(_secrets.ContainsKey(key));
    }
}
