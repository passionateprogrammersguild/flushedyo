using FlushedYo.Core.Configuration;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlushedYo.IntegrationTests;

internal static class StoreFactory
{
    public static BitwardenMcpSecretStore Create(BitwardenMcpOptions options) =>
        new(
            Options.Create(new FlushedYoOptions { SecretStore = new SecretStoreOptions { BitwardenMcp = options } }),
            NullLogger<BitwardenMcpSecretStore>.Instance);

    public static BitwardenMcpSecretStore CreateFake(string mode, int connectTimeout = 30, int callTimeout = 30) =>
        Create(new BitwardenMcpOptions
        {
            Command = "dotnet",
            Arguments = [HelperPaths.GetFakeServerDll(), mode],
            ConnectTimeoutSeconds = connectTimeout,
            CallTimeoutSeconds = callTimeout,
            EnvironmentVariables = new Dictionary<string, string>(),
        });
}
