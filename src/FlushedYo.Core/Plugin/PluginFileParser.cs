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
        var source = CreateSource(json, sourcePath);
        var dto = ParseManifestDto(source);

        ValidateName(dto.Name, source);
        ValidateVersion(dto.Version, source);
        var capabilities = ParseCapabilities(dto.Capabilities, source);
        var skills = ParseStringList(dto.Skills, "skills", source);
        var tools = ParseStringList(dto.Tools, "tools", source);
        ValidateAssemblyAndTypeDeclaredTogether(dto, source);

        return new PluginManifest
        {
            Name = dto.Name!,
            Version = dto.Version!,
            Capabilities = capabilities,
            Skills = skills,
            Tools = tools,
            Assembly = dto.Assembly is null ? null : ParseAssemblyPath(dto.Assembly, source),
            Type = dto.Type is null ? null : ParseTypeName(dto.Type, source),
        };
    }

    private static PluginFileSource CreateSource(string json, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(sourcePath);
        return new PluginFileSource(json, sourcePath);
    }

    private static ManifestDto ParseManifestDto(PluginFileSource source)
    {
        try
        {
            return JsonSerializer.Deserialize<ManifestDto>(source.Json, Options)
                ?? throw Reject(source, "the manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw Reject(source, ex.Message, ex);
        }
    }

    private static void ValidateName(string? name, PluginFileSource source)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Reject(source, "'name' is required.");
        }

        if (!NamePattern().IsMatch(name))
        {
            throw Reject(source, $"'name' must match {NamePatternText} (got '{name}').");
        }
    }

    private static void ValidateVersion(string? version, PluginFileSource source)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw Reject(source, "'version' is required.");
        }

        if (!VersionPattern().IsMatch(version))
        {
            throw Reject(source, $"'version' must be a SemVer string (got '{version}').");
        }
    }

    private static List<PluginCapability> ParseCapabilities(List<string?>? entries, PluginFileSource source)
    {
        var capabilities = new List<PluginCapability>(entries?.Count ?? 0);
        foreach (var entry in entries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw Reject(source, "'capabilities' entries must be non-empty strings.");
            }

            if (!PluginCapabilityNames.TryParse(entry, out var capability))
            {
                throw Reject(source, $"unknown capability '{entry}'.");
            }

            if (capabilities.Contains(capability))
            {
                throw Reject(source, $"duplicate capability '{entry}'.");
            }

            capabilities.Add(capability);
        }

        return capabilities;
    }

    private static void ValidateAssemblyAndTypeDeclaredTogether(ManifestDto dto, PluginFileSource source)
    {
        if ((dto.Assembly is null) != (dto.Type is null))
        {
            throw Reject(source, "'assembly' and 'type' must be declared together.");
        }
    }

    /// <summary>Parses and validates a <c>config.json</c> file.</summary>
    /// <param name="json">Raw JSON text of the configuration.</param>
    /// <param name="sourcePath">Path of the file, used only in error messages.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="PluginFileException">The JSON is malformed or fails validation.</exception>
    public static PluginConfiguration ParseConfiguration(string json, string sourcePath)
    {
        var source = CreateSource(json, sourcePath);
        ConfigurationDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ConfigurationDto>(json, Options)
                ?? throw Reject(source, "the configuration is empty.");
        }
        catch (JsonException ex)
        {
            throw Reject(source, ex.Message, ex);
        }

        return new PluginConfiguration
        {
            BitwardenMcpUrl = ParseUrl(dto.BitwardenMcpUrl, "bitwardenMcpUrl", source),
            PlaywrightMcpUrl = ParseUrl(dto.PlaywrightMcpUrl, "playwrightMcpUrl", source),
            Headless = dto.Headless ?? true,
            CredentialKeys = new PluginCredentialKeys
            {
                Username = dto.CredentialKeys?.Username,
                Password = dto.CredentialKeys?.Password,
            },
        };
    }

    private static List<string> ParseStringList(List<string?>? values, string propertyName, PluginFileSource source)
    {
        var result = new List<string>(values?.Count ?? 0);
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Reject(source, $"'{propertyName}' entries must be non-empty strings.");
            }

            var trimmed = value.Trim();
            if (result.Contains(trimmed, StringComparer.Ordinal))
            {
                throw Reject(source, $"duplicate '{propertyName}' entry '{trimmed}'.");
            }

            result.Add(trimmed);
        }

        return result;
    }

    private static string ParseAssemblyPath(string value, PluginFileSource source)
    {
        var trimmed = value.Trim();
        if (!IsRelativeDllPath(trimmed))
        {
            throw Reject(source, $"'assembly' must be a relative path to a .dll inside the plugin folder (got '{trimmed}').");
        }

        return trimmed.Replace('\\', '/');
    }

    private static bool IsRelativeDllPath(string value) =>
        HasNonEmptyValue(value)
        && IsRelative(value)
        && HasDllExtension(value)
        && HasNoTraversal(value)
        && HasNoInvalidCharacters(value);

    private static bool HasNonEmptyValue(string value) => value.Length > 0;

    private static bool IsRelative(string value) =>
        !Path.IsPathRooted(value) && !value.StartsWith('/') && !value.StartsWith('\\');

    private static bool HasDllExtension(string value) => value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static bool HasNoTraversal(string value) => !value.Split('/', '\\').Contains("..");

    private static bool HasNoInvalidCharacters(string value) =>
        !value.Contains(':', StringComparison.Ordinal) && !value.Contains('\0', StringComparison.Ordinal);
    private static string ParseTypeName(string value, PluginFileSource source)
    {
        var trimmed = value.Trim();
        if (!TypeNamePattern().IsMatch(trimmed))
        {
            throw Reject(source, $"'type' must be a full type name such as 'Company.Plugin.MyPlugin' (got '{trimmed}').");
        }

        return trimmed;
    }

    private static string? ParseUrl(string? value, string propertyName, PluginFileSource source)
    {
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsAbsoluteHttpUrl(uri))
        {
            throw Reject(source, $"'{propertyName}' must be an absolute http or https URL (got '{value}').");
        }

        return value;
    }

    internal static bool IsAbsoluteHttpUrl(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static PluginFileException Reject(PluginFileSource source, string reason, Exception? innerException = null) =>
        new($"Invalid plugin file '{source.SourcePath}': {reason}", source.SourcePath, innerException);

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

    /// <summary>Carries the JSON text and its path so error messages need no extra string parameter.</summary>
    private sealed record PluginFileSource(string Json, string SourcePath);
}
