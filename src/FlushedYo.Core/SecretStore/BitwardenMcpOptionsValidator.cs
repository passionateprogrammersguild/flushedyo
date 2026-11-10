using System.Text.RegularExpressions;
using FlushedYo.Core.Configuration;

namespace FlushedYo.Core.SecretStore;

/// <summary>
/// Validates <see cref="BitwardenMcpOptions"/> before the secret store is used. Every failure
/// throws <see cref="SecretStoreException"/> with the message the store has always produced.
/// </summary>
internal static class BitwardenMcpOptionsValidator
{
    /// <summary>Validates all option rules; returns on the first violation.</summary>
    public static void Validate(BitwardenMcpOptions options)
    {
        ValidateTransport(options);
        ValidateUrl(options);
        ValidateEnvironmentVariables(options);
    }

    private static void ValidateTransport(BitwardenMcpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Command) && string.IsNullOrWhiteSpace(options.Url))
        {
            throw new SecretStoreException(
                "The BitwardenMcp secret store requires either Command (stdio launch) or Url (HTTP endpoint) to be configured.");
        }
    }

    private static void ValidateUrl(BitwardenMcpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Url)) return;

        var isHttpOrHttps = Uri.TryCreate(options.Url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!isHttpOrHttps)
        {
            throw new SecretStoreException($"The BitwardenMcp Url '{options.Url}' must be an absolute HTTP or HTTPS URI.");
        }
    }

    private static void ValidateEnvironmentVariables(BitwardenMcpOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Url)) return;

        foreach (var (name, value) in options.EnvironmentVariables)
        {
            foreach (Match match in BitwardenMcpSecretStore.EnvironmentVariablePattern().Matches(value))
            {
                ThrowIfEnvironmentVariableMissing(match.Groups["Name"].Value);
            }
        }
    }

    private static void ThrowIfEnvironmentVariableMissing(string varName)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
        {
            throw new SecretStoreException($"Environment variable {varName} is not set; run 'bw unlock' and export {varName}.");
        }
    }
}
