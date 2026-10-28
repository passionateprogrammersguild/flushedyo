using System.Text.Json;

namespace FlushedYo.Tests.StubPlugin;

/// <summary>
/// Behavior of the stub plugin, read from <c>stub.json</c> in the plugin folder. Lets tests change what
/// the plugin does without rebuilding it. Unknown keys are ignored.
/// </summary>
public sealed class StubPluginData
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Transactions to return. Empty when omitted.</summary>
    public List<StubTransaction> Transactions { get; set; } = [];

    /// <summary>When set, <c>DownloadTransactionsAsync</c> throws an <see cref="InvalidOperationException"/> with this message.</summary>
    public string? Throw { get; set; }

    /// <summary>When set, overrides the plugin's reported version.</summary>
    public string? ReportVersion { get; set; }

    /// <summary>When set, a file with this name is written into the plugin folder on dispose.</summary>
    public string? DisposeMarker { get; set; }

    /// <summary>When set, the plugin checks whether this secret key exists and reports the result in an extra transaction.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Reads <c>stub.json</c> from <paramref name="pluginDirectory"/>; a missing file yields the defaults.</summary>
    public static StubPluginData Load(string pluginDirectory)
    {
        var path = Path.Combine(pluginDirectory, "stub.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<StubPluginData>(File.ReadAllText(path), Options) ?? new StubPluginData()
            : new StubPluginData();
    }
}

/// <summary>One transaction entry in <c>stub.json</c>.</summary>
public sealed class StubTransaction
{
    /// <summary>Institution; defaults to the plugin name when omitted.</summary>
    public string? Institution { get; set; }

    /// <summary>Institution-assigned identifier.</summary>
    public string ExternalId { get; set; } = "";

    /// <summary>Signed amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Transaction date.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Counterparty name.</summary>
    public string Payee { get; set; } = "";

    /// <summary>Free-form type.</summary>
    public string Type { get; set; } = "";
}
