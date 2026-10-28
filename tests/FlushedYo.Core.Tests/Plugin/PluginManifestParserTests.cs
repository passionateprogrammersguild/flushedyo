using FlushedYo.Core.Plugin;

namespace FlushedYo.Core.Tests.Plugin;

/// <summary>Validation rules for <c>plugin.json</c> (spec: plugin_manifest_models).</summary>
public sealed class PluginManifestParserTests
{
    private const string SourcePath = "/x/plugins/fidelity/plugin.json";

    [Fact]
    public void CompleteManifest_ParsesAllFields()
    {
        var manifest = PluginFileParser.ParseManifest(
            """
            {
              "name": "fidelity",
              "version": "1.0.0",
              "capabilities": ["download-transactions", "download-holdings"],
              "mcpServers": ["bitwarden", "playwright"],
              "skills": ["login", "navigate-accounts"],
              "tools": ["click", "type"]
            }
            """,
            SourcePath);

        Assert.Equal("fidelity", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal(
            [PluginCapability.DownloadTransactions, PluginCapability.DownloadHoldings],
            manifest.Capabilities.ToArray());
        Assert.Equal(["bitwarden", "playwright"], manifest.McpServers.ToArray());
        Assert.Equal(["login", "navigate-accounts"], manifest.Skills.ToArray());
        Assert.Equal(["click", "type"], manifest.Tools.ToArray());
    }

    [Fact]
    public void MinimalManifest_YieldsEmptyCollections()
    {
        var manifest = PluginFileParser.ParseManifest(Minimal(), SourcePath);

        Assert.Empty(manifest.Capabilities);
        Assert.Empty(manifest.McpServers);
        Assert.Empty(manifest.Skills);
        Assert.Empty(manifest.Tools);
    }

    [Theory]
    [InlineData("download-transactions", PluginCapability.DownloadTransactions)]
    [InlineData("download-holdings", PluginCapability.DownloadHoldings)]
    public void KnownCapability_ParsesToEnumMember(string capability, PluginCapability expected)
    {
        var manifest = PluginFileParser.ParseManifest(
            $$"""{ "name": "fidelity", "version": "1.0.0", "capabilities": ["{{capability}}"] }""",
            SourcePath);

        Assert.Equal([expected], manifest.Capabilities.ToArray());
    }

    [Fact]
    public void UnknownCapability_Throws()
    {
        var exception = Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "capabilities": ["download-everything"] }""",
            SourcePath));

        Assert.Contains("download-everything", exception.Message, StringComparison.Ordinal);
        Assert.Contains(SourcePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityCasing_IsCaseSensitive()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "capabilities": ["Download-Transactions"] }""",
            SourcePath));
    }

    [Fact]
    public void DuplicateCapability_Throws()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "capabilities": ["download-transactions", "download-transactions"] }""",
            SourcePath));
    }

    [Fact]
    public void MissingName_Throws()
    {
        var exception = Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "version": "1.0.0" }""",
            SourcePath));

        Assert.Contains("'name' is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingVersion_Throws()
    {
        var exception = Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity" }""",
            SourcePath));

        Assert.Contains("'version' is required", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    public void InvalidVersion_Throws(string version)
    {
        var exception = Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest(Minimal(version: version), SourcePath));

        Assert.Contains("SemVer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrereleaseVersion_IsAccepted()
    {
        var manifest = PluginFileParser.ParseManifest(Minimal(version: "1.2.3-beta.1"), SourcePath);

        Assert.Equal("1.2.3-beta.1", manifest.Version);
    }

    [Fact]
    public void UppercaseName_Throws()
    {
        Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest(Minimal(name: "Fidelity"), SourcePath));
    }

    [Fact]
    public void UnderscoreName_Throws()
    {
        Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest(Minimal(name: "my_plugin"), SourcePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSkillEntry_Throws(string entry)
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            $$"""{ "name": "fidelity", "version": "1.0.0", "skills": ["{{entry}}"] }""",
            SourcePath));
    }

    [Fact]
    public void DuplicateSkillEntry_Throws()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "skills": ["login", "login"] }""",
            SourcePath));
    }

    [Fact]
    public void UnknownTopLevelProperty_IsIgnored()
    {
        var manifest = PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "author": "x" }""",
            SourcePath);

        Assert.Equal("fidelity", manifest.Name);
    }

    [Fact]
    public void MalformedJson_ThrowsWithSourcePath()
    {
        var exception = Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest("""{ "name": """, SourcePath));

        Assert.Contains(SourcePath, exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void CapabilitiesAsString_Throws()
    {
        Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "capabilities": "download-transactions" }""",
            SourcePath));
    }

    [Fact]
    public void EntriesAreTrimmed()
    {
        var manifest = PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "skills": ["  login  "] }""",
            SourcePath);

        Assert.Equal(["login"], manifest.Skills.ToArray());
    }

    [Fact]
    public void AssemblyAndType_AreParsed()
    {
        var manifest = PluginFileParser.ParseManifest(
            WithEntryPoint("Stub.dll", "Company.Plugin.MyPlugin"),
            SourcePath);

        Assert.Equal("Stub.dll", manifest.Assembly);
        Assert.Equal("Company.Plugin.MyPlugin", manifest.Type);
    }

    [Fact]
    public void NeitherAssemblyNorType_YieldsNulls()
    {
        var manifest = PluginFileParser.ParseManifest(Minimal(), SourcePath);

        Assert.Null(manifest.Assembly);
        Assert.Null(manifest.Type);
    }

    [Fact]
    public void OnlyAssembly_Throws()
    {
        var exception = Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "assembly": "Stub.dll" }""",
            SourcePath));

        Assert.Contains("declared together", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyType_Throws()
    {
        var exception = Assert.Throws<PluginFileException>(() => PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "type": "A.B" }""",
            SourcePath));

        Assert.Contains("declared together", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bin/Stub.dll")]
    [InlineData(@"bin\\Stub.dll")]
    public void AssemblyInSubfolder_IsAcceptedAndNormalizedToForwardSlashes(string jsonValue)
    {
        var manifest = PluginFileParser.ParseManifest(WithEntryPoint(jsonValue, "A.B"), SourcePath);

        Assert.Equal("bin/Stub.dll", manifest.Assembly);
    }

    [Fact]
    public void AssemblyExtension_IsMatchedCaseInsensitively()
    {
        Assert.Equal("STUB.DLL", PluginFileParser.ParseManifest(WithEntryPoint("STUB.DLL", "A.B"), SourcePath).Assembly);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/abs/Stub.dll")]
    [InlineData("C:/x/Stub.dll")]
    [InlineData(@"..\\Stub.dll")]
    [InlineData("a/../b.dll")]
    [InlineData("Stub.exe")]
    [InlineData("Stub")]
    public void InvalidAssembly_Throws(string jsonValue)
    {
        var exception = Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest(WithEntryPoint(jsonValue, "A.B"), SourcePath));

        Assert.Contains("'assembly' must be a relative path to a .dll", exception.Message, StringComparison.Ordinal);
        Assert.Contains(SourcePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidAssembly_MessageNamesTheOffendingValue()
    {
        var exception = Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest(WithEntryPoint("Stub.exe", "A.B"), SourcePath));

        Assert.Contains("Stub.exe", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Bad")]
    [InlineData("A..B")]
    [InlineData("A.B+Nested")]
    [InlineData("A.B<T>")]
    [InlineData("A B")]
    public void InvalidType_Throws(string typeName)
    {
        var exception = Assert.Throws<PluginFileException>(() =>
            PluginFileParser.ParseManifest(WithEntryPoint("Stub.dll", typeName), SourcePath));

        Assert.Contains("'type' must be a full type name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullAssemblyAndType_AreTreatedAsAbsent()
    {
        var manifest = PluginFileParser.ParseManifest(
            """{ "name": "fidelity", "version": "1.0.0", "assembly": null, "type": null }""",
            SourcePath);

        Assert.Null(manifest.Assembly);
        Assert.Null(manifest.Type);
    }

    private static string WithEntryPoint(string assembly, string type) =>
        $$"""{ "name": "fidelity", "version": "1.0.0", "assembly": "{{assembly}}", "type": "{{type}}" }""";

    private static string Minimal(string name = "fidelity", string version = "1.0.0") =>
        $$"""{ "name": "{{name}}", "version": "{{version}}" }""";
}
