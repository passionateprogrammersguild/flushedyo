using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.PropertyTests.SecretStore;

/// <summary>
/// Property family for the environment-variable secret store (spec: secret_store).
/// The key-to-variable mapping is a permanent integration contract
/// (<c>FLUSHEDYO_SECRET_</c> + upper-cased name with <c>/</c> and <c>-</c> mapped to <c>_</c>);
/// the property pins that mapping and round-trips generated keys through the real environment.
/// Serialized with the other tests that mutate process-wide environment variables.
/// </summary>
[Collection("EnvironmentMutation")]
public sealed class EnvironmentVariableSecretStoreProperties
{
    private const string Sentinel = "sentinel-value-9f3a";

    [Property(MaxTest = 50)]
    public Property KeyMapping_IsDeterministicAndRoundTripsThroughTheEnvironment()
    {
        var token = TestContext.Current.CancellationToken;
        return Prop.ForAll(Key().ToArbitrary(), async key =>
        {
            var store = new EnvironmentVariableSecretStore();
            var variable = EnvironmentVariableSecretStore.ToVariableName(key);

            // Pins the documented mapping; the variable name is derived from the key alone.
            var expected = "FLUSHEDYO_SECRET_" + key.Trim().Replace('/', '_').Replace('-', '_').ToUpperInvariant();
            if (variable != expected)
            {
                return false;
            }

            try
            {
                // Unset: the key resolves to no secret, and SecretExistsAsync mirrors that.
                Environment.SetEnvironmentVariable(variable, null);
                if (await store.GetSecretAsync(key, token) is not null || await store.SecretExistsAsync(key, token))
                {
                    return false;
                }

                // Set: GetSecretAsync returns the sentinel and SecretExistsAsync mirrors it.
                Environment.SetEnvironmentVariable(variable, Sentinel);
                return await store.GetSecretAsync(key, token) == Sentinel
                    && await store.SecretExistsAsync(key, token);
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        });
    }

    /// <summary>A key built from the <c>[a-z0-9/-]</c> alphabet the mapping documents.</summary>
    private static Gen<string> Key() =>
        from length in Gen.Choose(1, 12)
        from characters in Gen.ArrayOf(Gen.Elements(KeyCharacters), length)
        select new string(characters);

    private static readonly char[] KeyCharacters = "abcdefghijklmnopqrstuvwxyz0123456789/-".ToCharArray();
}
