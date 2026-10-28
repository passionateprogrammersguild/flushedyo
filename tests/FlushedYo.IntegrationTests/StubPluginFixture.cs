using System.Text;
using FlushedYo.Core.Plugin;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Builds plugin folders that use the stub plugin assembly: the same shape a real plugin would have
/// (manifest, assembly, optional config), controlled by a <c>stub.json</c> data file.
/// </summary>
internal static class StubPluginFixture
{
    public const string AssemblyFileName = "FlushedYo.Tests.StubPlugin.dll";
    public const string DefaultType = "FlushedYo.Tests.StubPlugin.StubProviderPlugin";

    private const string DepsFileName = "FlushedYo.Tests.StubPlugin.deps.json";

    /// <summary>
    /// Creates <c>&lt;pluginsRoot&gt;/&lt;name&gt;/</c> with <c>plugin.json</c>, a copy of the stub assembly and its
    /// deps file, and <c>stub.json</c> when <paramref name="stubJson"/> is given. <c>FlushedYo.Core.dll</c> is
    /// deliberately not copied: a real plugin resolves it from the host.
    /// </summary>
    public static string CreatePlugin(
        string pluginsRoot,
        string name,
        string? stubJson = null,
        string type = DefaultType,
        string version = "1.0.0",
        string capabilitiesJson = "[\"download-transactions\"]",
        string? credentialKeysJson = null)
    {
        var folder = Path.Combine(pluginsRoot, name);
        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            $$"""
            {
              "name": "{{name}}",
              "version": "{{version}}",
              "capabilities": {{capabilitiesJson}},
              "assembly": "{{AssemblyFileName}}",
              "type": "{{type}}"
            }
            """);

        var stubDirectory = HelperPaths.GetStubPluginDirectory();
        File.Copy(Path.Combine(stubDirectory, AssemblyFileName), Path.Combine(folder, AssemblyFileName), overwrite: true);
        File.Copy(Path.Combine(stubDirectory, DepsFileName), Path.Combine(folder, DepsFileName), overwrite: true);

        if (stubJson is not null)
        {
            File.WriteAllText(Path.Combine(folder, "stub.json"), stubJson);
        }

        if (credentialKeysJson is not null)
        {
            File.WriteAllText(Path.Combine(folder, "config.json"), $$"""{ "credentialKeys": {{credentialKeysJson}} }""");
        }

        return folder;
    }

    /// <summary>Parses the folder's <c>plugin.json</c> (and <c>config.json</c> if present) into a descriptor.</summary>
    public static PluginDescriptor CreateDescriptor(string pluginDirectory)
    {
        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        var manifest = PluginFileParser.ParseManifest(File.ReadAllText(manifestPath), manifestPath);

        var configurationPath = Path.Combine(pluginDirectory, "config.json");
        var configuration = File.Exists(configurationPath)
            ? PluginFileParser.ParseConfiguration(File.ReadAllText(configurationPath), configurationPath)
            : PluginConfiguration.Default;

        return new PluginDescriptor(manifest, configuration, pluginDirectory);
    }

    /// <summary>
    /// Builds a <c>stub.json</c> document with <paramref name="count"/> transactions (<c>externalId</c>
    /// "<c>idPrefix-<i>0..count-1></c>", <c>amount</c> "-<i>N</i>.50", fixed date, payee "<c>Payee <i>N</i></c>",
    /// type <c>debit</c>), each carrying <paramref name="institution"/> when provided.
    /// </summary>
    public static string TransactionsJson(int count, string? institution = null, string idPrefix = "tx")
    {
        var entries = new StringBuilder();
        entries.Append("{\"transactions\":[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                entries.Append(',');
            }

            entries.Append("{\"externalId\":\"").Append(escape(idPrefix)).Append('-').Append(i)
                .Append("\",\"amount\":-").Append(i).Append(".50")
                .Append(",\"date\":\"2026-01-15\"")
                .Append(",\"payee\":\"Payee ").Append(i).Append('"')
                .Append(",\"type\":\"debit\"");
            if (institution is not null)
            {
                entries.Append(",\"institution\":\"").Append(escape(institution)).Append('"');
            }

            entries.Append('}');
        }

        entries.Append("]}");
        return entries.ToString();

        static string escape(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a plugin folder with a descriptor-only <c>plugin.json</c> (no <c>assembly</c>/<c>type</c>)
    /// that declares the <c>download-transactions</c> capability.
    /// </summary>
    public static string CreateDescriptorOnlyPlugin(string pluginsRoot, string name)
    {
        var folder = Path.Combine(pluginsRoot, name);
        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "plugin.json"),
            $$"""
            {
              "name": "{{name}}",
              "version": "1.0.0",
              "capabilities": ["download-transactions"]
            }
            """);

        return folder;
    }

}
