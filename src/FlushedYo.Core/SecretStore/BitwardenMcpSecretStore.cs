using System.Text.Json;
using System.Text.RegularExpressions;
using FlushedYo.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FlushedYo.Core.SecretStore;

/// <summary>
/// <see cref="ISecretStore"/> implementation backed by the official Bitwarden MCP server
/// (https://github.com/bitwarden/mcp-server), connected through the official
/// ModelContextProtocol C# SDK.
///
/// Two transports are supported:
///  - stdio: launches the server via <see cref="BitwardenMcpOptions.Command"/>
///    (default: npx -y @bitwarden/mcp-server)
///  - http: connects to an already-running server via <see cref="BitwardenMcpOptions.Url"/>
///
/// Secrets are resolved by invoking the configured tool with the secret key plus any static
/// <see cref="BitwardenMcpOptions.ToolArguments"/>. The defaults target the official server's
/// <c>get</c> tool, equivalent to <c>bw get password &lt;id&gt;</c>.
/// </summary>
public sealed partial class BitwardenMcpSecretStore : ISecretStore
{
    private readonly BitwardenMcpOptions _options;
    private readonly Func<CancellationToken, Task<McpClient>> _clientFactory;
    private readonly ILogger _logger;

    /// <summary>Creates the store from application configuration.</summary>
    public BitwardenMcpSecretStore(IOptions<FlushedYoOptions> options, ILogger<BitwardenMcpSecretStore> logger)
        : this(options.Value.SecretStore.BitwardenMcp, logger)
    {
    }

    internal BitwardenMcpSecretStore(BitwardenMcpOptions options, ILogger logger)
        : this(options, CreateTransportFactory(options, logger), logger)
    {
    }

    internal BitwardenMcpSecretStore(BitwardenMcpOptions options, Func<CancellationToken, Task<McpClient>> clientFactory, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ValidateOptions();
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Command) && string.IsNullOrWhiteSpace(_options.Url))
        {
            throw new SecretStoreException(
                "The BitwardenMcp secret store requires either Command (stdio launch) or Url (HTTP endpoint) to be configured.");
        }

        if (!string.IsNullOrWhiteSpace(_options.Url))
        {
            if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new SecretStoreException($"The BitwardenMcp Url '{_options.Url}' must be an absolute HTTP or HTTPS URI.");
            }
        }

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            foreach (var (name, value) in _options.EnvironmentVariables)
            {
                var matches = EnvironmentVariablePattern().Matches(value);
                foreach (Match match in matches)
                {
                    var varName = match.Groups["Name"].Value;
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
                    {
                        throw new SecretStoreException($"Environment variable {varName} is not set; run 'bw unlock' and export {varName}.");
                    }
                }
            }
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logResolvingSecret(_logger, key, _options.GetSecretTool, null);
        }

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

        McpClient? client = null;
        try
        {
            client = await _clientFactory(connectCts.Token);
        }
        catch (OperationCanceledException) when (connectCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreException($"Timed out after {_options.ConnectTimeoutSeconds}s connecting to the Bitwarden MCP server.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapConnectionException(ex, _options.Command ?? "Unknown");
        }

        try
        {
            await using (client)
            {
                // Fix: Stop the connect timer so it doesn't kill the session
                connectCts.CancelAfter(Timeout.InfiniteTimeSpan);

                // Preflight: Check if vault is locked
                await PerformPreflightCheckAsync(client, cancellationToken);

                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                callCts.CancelAfter(TimeSpan.FromSeconds(_options.CallTimeoutSeconds));

                var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (name, value) in _options.ToolArguments)
                {
                    arguments[name] = value;
                }

                arguments[_options.SecretKeyArgument] = key;

                var result = await client.CallToolAsync(_options.GetSecretTool, arguments, cancellationToken: callCts.Token);

                if (result.IsError is true)
                {
                    var errorText = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                    if (IsNotFound(errorText))
                    {
                        return null;
                    }
                    throw new SecretStoreException(
                        $"Bitwarden MCP tool '{_options.GetSecretTool}' failed for '{key}'" +
                        (string.IsNullOrWhiteSpace(errorText) ? "." : $": {errorText}"));
                }

                var text = string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(block => block.Text));
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreException($"Timed out after {_options.CallTimeoutSeconds}s waiting for Bitwarden MCP tool '{_options.GetSecretTool}'.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or SecretStoreException))
        {
            throw WrapCallException(ex);
        }
    }

    private async Task PerformPreflightCheckAsync(McpClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.PreflightStatusTool)) return;

        using var preflightCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        preflightCts.CancelAfter(TimeSpan.FromSeconds(_options.CallTimeoutSeconds));

        try
        {
            var result = await client.CallToolAsync(_options.PreflightStatusTool, new Dictionary<string, object?>(), cancellationToken: preflightCts.Token);
            if (result.IsError == true) return;

            var text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
            if (string.IsNullOrWhiteSpace(text)) return;

            // Try JSON parsing first
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("status", out var statusProp))
                {
                    var status = statusProp.GetString();
                    if (string.Equals(status, "locked", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "unauthenticated", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SecretStoreException("Bitwarden vault is locked. Run 'bw unlock --raw' and export BW_SESSION.");
                    }
                }
            }
            catch (JsonException)
            {
                // Fallback to regex for non-JSON or malformed JSON
                var match = StatusRegex().Match(text);
                if (match.Success)
                {
                    var status = match.Groups["s"].Value;
                    if (string.Equals(status, "locked", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "unauthenticated", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SecretStoreException("Bitwarden vault is locked. Run 'bw unlock --raw' and export BW_SESSION.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SecretStoreException($"Preflight check timed out after {_options.CallTimeoutSeconds}s.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretStoreException) { throw; }
        catch { /* Ignore other preflight failures and proceed to the actual call */ }
    }

    private static bool IsNotFound(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return NotFoundRegex().IsMatch(text);
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
        => await GetSecretAsync(key, cancellationToken) is not null;

    private static SecretStoreException WrapConnectionException(Exception ex, string command)
    {
        var closed = FindInChain<ClientTransportClosedException>(ex);
        if (closed?.Details is StdioClientCompletionDetails stdioDetails)
        {
            var stderr = stdioDetails.StandardErrorTail is { Count: > 0 } tail
                ? string.Join(Environment.NewLine, tail.TakeLast(10))
                : "none";
            if (stderr.Length > 1024)
            {
                stderr = stderr[^1024..];
            }

            return new SecretStoreException(
                $"Bitwarden MCP server exited during startup (exit code {stdioDetails.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}). " +
                $"Last stderr: {stderr}", ex);
        }

        if (FindInChain<HttpRequestException>(ex) is { } httpEx)
        {
            return new SecretStoreException($"Cannot reach Bitwarden MCP server: {httpEx.Message}", ex);
        }

        if (IsMissingCommandException(ex))
        {
            return new SecretStoreException($"Could not start Bitwarden MCP server: command '{command}' was not found. Install it (Node.js provides 'npx') or set FlushedYo:SecretStore:BitwardenMcp:Command.", ex);
        }

        var innermost = ex;
        while (innermost.InnerException != null) innermost = innermost.InnerException!;
        return new SecretStoreException($"Failed to connect to Bitwarden MCP server: {innermost.Message}", ex);
    }

    private static bool IsMissingCommandException(Exception ex)
        => FindInChain<System.ComponentModel.Win32Exception>(ex) is not null || FindInChain<FileNotFoundException>(ex) is not null;

    private static T? FindInChain<T>(Exception? ex) where T : Exception
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is T match) return match;
        }
        return null;
    }

    private static SecretStoreException WrapCallException(Exception ex)
    {
        if (ex is ClientTransportClosedException closedEx && closedEx.Details is StdioClientCompletionDetails stdioDetails)
        {
            return new SecretStoreException($"Bitwarden MCP server crashed during call (exit code {stdioDetails.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}).", ex);
        }

        var innermost = ex;
        while (innermost.InnerException != null) innermost = innermost.InnerException!;
        return new SecretStoreException($"Bitwarden MCP call failed: {innermost.Message}", ex);
    }

    private static Func<CancellationToken, Task<McpClient>> CreateTransportFactory(BitwardenMcpOptions options, ILogger logger)
    {
        return async cancellationToken =>
        {
            if (!string.IsNullOrWhiteSpace(options.Url))
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    _logConnectingHttp(logger, options.Url, null);
                }
                var httpTransport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(options.Url),
                });
                return await McpClient.CreateAsync(httpTransport, cancellationToken: cancellationToken);
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                _logStartingStdio(logger, options.Command ?? "Unknown", string.Join(' ', options.Arguments), null);
            }

            var stdioTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "bitwarden",
                Command = options.Command!,
                Arguments = options.Arguments.ToArray(),
                EnvironmentVariables = ExpandEnvironmentVariables(options.EnvironmentVariables),
            });

            return await McpClient.CreateAsync(stdioTransport, cancellationToken: cancellationToken);
        };
    }

    internal static Dictionary<string, string?> ExpandEnvironmentVariables(IReadOnlyDictionary<string, string> configured)
    {
        var expanded = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in configured)
        {
            expanded[name] = EnvironmentVariablePattern().Replace(value, match =>
            {
                var environmentValue = Environment.GetEnvironmentVariable(match.Groups["Name"].Value);
                return environmentValue ?? match.Value;
            });
        }

        return expanded;
    }

    [GeneratedRegex(@"\$\{(?<Name>[A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvironmentVariablePattern();

    [GeneratedRegex(@"\bnot found\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotFoundRegex();

    [GeneratedRegex(@"\""status\""\s*:\s*\""(?<s>[A-Za-z]+)\""", RegexOptions.IgnoreCase)]
    private static partial Regex StatusRegex();

    private static readonly Action<ILogger, string, string, Exception?> _logResolvingSecret =
        LoggerMessage.Define<string, string>(LogLevel.Debug, 0, "Resolving secret '{SecretKey}' via Bitwarden MCP tool '{ToolName}'.");

    private static readonly Action<ILogger, string, Exception?> _logConnectingHttp =
        LoggerMessage.Define<string>(LogLevel.Information, 0, "Connecting to Bitwarden MCP server at {Url}.");

    private static readonly Action<ILogger, string, string, Exception?> _logStartingStdio =
        LoggerMessage.Define<string, string>(LogLevel.Information, 0, "Starting Bitwarden MCP server: {Command} {Arguments}");
}
