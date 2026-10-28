using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// Parses and validates the JSON content of a plugin's <c>plugin.json</c> manifest and optional
/// <c>config.json</c> file. This type performs no file I/O; callers read the text and pass it in.
/// </summary>
public static partial class PluginFileParser
{
    private const string NamePatternText = "^[a-z0-9][a-z0-9-]*$";
    private const string VersionPatternText = @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$";
    private const string TypeNamePatternText = @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Parses and validates a <c>plugin.json</c> manifest.</summary>
    /// <param name="json">Raw JSON text of the manifest.</param>
    /// <param name="sourcePath">Path of the file, used only in error messages.</param>
    /// <returns>The validated manifest.</returns>
    /// <exception cref="PluginFileException">The JSON is malformed or fails validation.</exception>
    public static PluginManifest ParseManifest(string json, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(sourcePath);

        ManifestDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, Options)
                ?? throw Fail(sourcePath, "the manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw Fail(sourcePath, ex.Message, ex);
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw Fail(sourcePath, "'name' is required.");
        }

        if (!NamePattern().IsMatch(dto.Name))
        {
            throw Fail(sourcePath, $"'name' must match {NamePatternText} (got '{dto.Name}').");
        }

        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            throw Fail(sourcePath, "'version' is required.");
        }

        if (!VersionPattern().IsMatch(dto.Version))
        {
            throw Fail(sourcePath, $"'version' must be a SemVer string (got '{dto.Version}').");
        }

        var capabilities = new List<PluginCapability>(dto.Capabilities?.Count ?? 0);
        foreach (var entry in dto.Capabilities ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw Fail(sourcePath, "'capabilities' entries must be non-empty strings.");
            }

            if (!PluginCapabilityNames.TryParse(entry, out var capability))
            {
                throw Fail(sourcePath, $"unknown capability '{entry}'.");
            }

            if (capabilities.Contains(capability))
            {
                throw Fail(sourcePath, $"duplicate capability '{entry}'.");
            }

            capabilities.Add(capability);
        }

        var mcpServers = ParseStringList(dto.McpServers, "mcpServers", sourcePath);
        var skills = ParseStringList(dto.Skills, "skills", sourcePath);
        var tools = ParseStringList(dto.Tools, "tools", sourcePath);

        if ((dto.Assembly is null) != (dto.Type is null))
        {
            throw Fail(sourcePath, "'assembly' and 'type' must be declared together.");
        }

        return new PluginManifest
        {
            Name = dto.Name,
            Version = dto.Version,
            Capabilities = capabilities,
            McpServers = mcpServers,
            Skills = skills,
            Tools = tools,
            Assembly = dto.Assembly is null ? null : ParseAssemblyPath(dto.Assembly, sourcePath),
            Type = dto.Type is null ? null : ParseTypeName(dto.Type, sourcePath),
        };
    }

    /// <summary>Parses and validates a <c>config.json</c> file.</summary>
    /// <param name="json">Raw JSON text of the configuration.</param>
    /// <param name="sourcePath">Path of the file, used only in error messages.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="PluginFileException">The JSON is malformed or fails validation.</exception>
    public static PluginConfiguration ParseConfiguration(string json, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(sourcePath);

        ConfigurationDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ConfigurationDto>(json, Options)
                ?? throw Fail(sourcePath, "the configuration is empty.");
        }
        catch (JsonException ex)
        {
            throw Fail(sourcePath, ex.Message, ex);
        }

        return new PluginConfiguration
        {
            BitwardenMcpUrl = ParseUrl(dto.BitwardenMcpUrl, "bitwardenMcpUrl", sourcePath),
            PlaywrightMcpUrl = ParseUrl(dto.PlaywrightMcpUrl, "playwrightMcpUrl", sourcePath),
            Headless = dto.Headless ?? true,
            CredentialKeys = new PluginCredentialKeys
            {
                Username = dto.CredentialKeys?.Username,
                Password = dto.CredentialKeys?.Password,
            },
        };
    }

    private static List<string> ParseStringList(List<string?>? values, string propertyName, string sourcePath)
    {
        var result = new List<string>(values?.Count ?? 0);
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Fail(sourcePath, $"'{propertyName}' entries must be non-empty strings.");
            }

            var trimmed = value.Trim();
            if (result.Contains(trimmed, StringComparer.Ordinal))
            {
                throw Fail(sourcePath, $"duplicate '{propertyName}' entry '{trimmed}'.");
            }

            result.Add(trimmed);
        }

        return result;
    }

    private static string ParseAssemblyPath(string value, string sourcePath)
    {
        var trimmed = value.Trim();
        var segments = trimmed.Split('/', '\\');

        var valid = trimmed.Length > 0
            && !Path.IsPathRooted(trimmed)
            && !trimmed.StartsWith('/')
            && !trimmed.StartsWith('\\')
            && !segments.Contains("..")
            && trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains(':', StringComparison.Ordinal)
            && !trimmed.Contains('\0', StringComparison.Ordinal);

        if (!valid)
        {
            throw Fail(sourcePath, $"'assembly' must be a relative path to a .dll inside the plugin folder (got '{trimmed}').");
        }

        return trimmed.Replace('\\', '/');
    }

    private static string ParseTypeName(string value, string sourcePath)
    {
        var trimmed = value.Trim();
        if (!TypeNamePattern().IsMatch(trimmed))
        {
            throw Fail(sourcePath, $"'type' must be a full type name such as 'Company.Plugin.MyPlugin' (got '{trimmed}').");
        }

        return trimmed;
    }

    private static string? ParseUrl(string? value, string propertyName, string sourcePath)
    {
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw Fail(sourcePath, $"'{propertyName}' must be an absolute http or https URL (got '{value}').");
        }

        return value;
    }

    private static PluginFileException Fail(string sourcePath, string reason, Exception? innerException = null) =>
        new($"Invalid plugin file '{sourcePath}': {reason}", sourcePath, innerException);

    [GeneratedRegex(NamePatternText)]
    private static partial Regex NamePattern();

    [GeneratedRegex(VersionPatternText)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(TypeNamePatternText)]
    private static partial Regex TypeNamePattern();

    private sealed class ManifestDto
    {
        public string? Name { get; set; }

        public string? Version { get; set; }

        public List<string?>? Capabilities { get; set; }

        public List<string?>? McpServers { get; set; }

        public List<string?>? Skills { get; set; }

        public List<string?>? Tools { get; set; }

        public string? Assembly { get; set; }

        public string? Type { get; set; }
    }

    private sealed class ConfigurationDto
    {
        public string? BitwardenMcpUrl { get; set; }

        public string? PlaywrightMcpUrl { get; set; }

        public bool? Headless { get; set; }

        public CredentialKeysDto? CredentialKeys { get; set; }
    }

    private sealed class CredentialKeysDto
    {
        public string? Username { get; set; }

        public string? Password { get; set; }
    }
}
