using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>
/// An <see cref="ISecretStore"/> for tests. <see cref="GetSecretAsync"/> throws on purpose: code that must
/// only check existence (such as <c>login</c>) fails loudly if it ever asks for a value.
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

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _checkedKeys.Add(key);
        return existsFailure is not null
            ? throw existsFailure
            : Task.FromResult(_secrets.ContainsKey(key));
    }
}
