namespace FlushedYo.Core.AgentBridge;

/// <summary>Exact-origin navigation policy. No hostname suffix, wildcard, or page-text matching is used.</summary>
internal sealed class ProviderBrowserPolicy
{
    private readonly HashSet<string> _allowedOrigins;

    internal ProviderBrowserPolicy(IEnumerable<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);
        _allowedOrigins = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in allowedOrigins)
        {
            if (!TryParseOrigin(entry, out var origin) || !IsPermittedScheme(origin))
            {
                throw new BridgePolicyException("A browser allowlist entry is not a valid permitted origin.");
            }

            _allowedOrigins.Add(OriginKey(origin));
        }
    }

    internal bool IsAllowed(Uri? uri) =>
        uri is not null &&
        uri.IsAbsoluteUri &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        IsPermittedScheme(uri) &&
        _allowedOrigins.Contains(OriginKey(uri));

    internal string GetOrigin(Uri uri)
    {
        if (!IsAllowed(uri))
        {
            throw new BridgePolicyException("The browser origin is not allowed.");
        }

        return OriginKey(uri);
    }

    internal static string OriginKey(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !IsHttp(uri))
        {
            throw new BridgePolicyException("The browser origin is not an absolute HTTP origin.");
        }

        var port = uri.IsDefaultPort ? DefaultPort(uri.Scheme) : uri.Port;
        return $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}:{port}";
    }

    private static bool TryParseOrigin(string? entry, out Uri origin)
    {
        origin = null!;
        if (string.IsNullOrWhiteSpace(entry) ||
            !Uri.TryCreate(entry, UriKind.Absolute, out origin!) ||
            !IsHttp(origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) ||
            origin.AbsolutePath != "/")
        {
            return false;
        }

        return true;
    }

    private static bool IsPermittedScheme(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return IsHttp(uri);
        }

        return uri.Scheme == Uri.UriSchemeHttp &&
               (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static int DefaultPort(string scheme) => scheme == Uri.UriSchemeHttps ? 443 : 80;
}
