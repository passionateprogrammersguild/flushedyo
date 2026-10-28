using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlushedYo.Core.Commands;

/// <summary>Serializes command output as JSON with one shared set of conventions.</summary>
public static class CliJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Serializes <paramref name="value"/> with camelCase property names, indentation, enums as camelCase
    /// strings, and nulls written explicitly so field sets are stable.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The JSON text without a trailing newline (the command writes the line).</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
