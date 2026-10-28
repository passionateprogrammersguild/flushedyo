using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.IntegrationTests;

/// <summary>
/// Loads the real stub plugin assembly into isolated load contexts (spec: plugin_assembly_loading).
/// These need the built stub, so they live with the other tests that use helper binaries.
/// </summary>
public sealed class PluginAssemblyLoaderTests
{
    private const string SecretValue = "S3CR3T-VALUE";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PluginAssemblyLoader CreateLoader(params string[] existingSecretKeys) =>
        new(new SetSecretStore(existingSecretKeys), NullLoggerFactory.Instance);

    [Fact]
    public async Task LoadsStubPlugin_AndReturnsTheTransactionsFromStubJson()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", StubJson());

        await using var loaded = CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder));
        var transactions = (await loaded.Plugin.DownloadTransactionsAsync(Token)).ToList();

        Assert.Equal("stub", loaded.Plugin.Name);
        Assert.Equal("1.0.0", loaded.Plugin.Version);
        var transaction = Assert.Single(transactions);
        Assert.Equal("fidelity", transaction.Institution);
        Assert.Equal("abc-123", transaction.ExternalId);
        Assert.Equal(-12.30m, transaction.Amount);
        Assert.Equal(new DateOnly(2026, 1, 2), transaction.Date);
        Assert.Equal("Corner Store", transaction.Payee);
        Assert.Equal("debit", transaction.Type);
    }

    [Fact]
    public async Task Institution_DefaultsToThePluginName()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(
            root.Path,
            "stub",
            """{ "transactions": [ { "externalId": "1", "amount": 5, "date": "2026-01-01", "payee": "P", "type": "t" } ] }""");

        await using var loaded = CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder));

        Assert.Equal("stub", Assert.Single(await loaded.Plugin.DownloadTransactionsAsync(Token)).Institution);
    }

    [Theory]
    [InlineData(true, "secret:present")]
    [InlineData(false, "secret:absent")]
    public async Task PluginReachesTheSecretStoreThroughItsContext(bool secretExists, string expectedPayee)
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", """{ "secretKey": "stub/username" }""");
        var loader = secretExists ? CreateLoader("stub/username") : CreateLoader();

        await using var loaded = loader.Load(StubPluginFixture.CreateDescriptor(folder));
        var transactions = (await loaded.Plugin.DownloadTransactionsAsync(Token)).ToList();

        var check = Assert.Single(transactions);
        Assert.Equal("secret-check", check.ExternalId);
        Assert.Equal(expectedPayee, check.Payee);
    }

    [Fact]
    public async Task SecretValue_NeverAppearsInReturnedTransactions()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", StubJson(secretKey: "stub/username"));

        await using var loaded = CreateLoader("stub/username").Load(StubPluginFixture.CreateDescriptor(folder));
        var transactions = await loaded.Plugin.DownloadTransactionsAsync(Token);

        Assert.All(transactions, transaction => Assert.DoesNotContain(SecretValue, transaction.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void DescriptorWithoutAssembly_Throws()
    {
        using var root = new TempRoot();
        var descriptor = new PluginDescriptor(
            new PluginManifest { Name = "stub", Version = "1.0.0" },
            PluginConfiguration.Default,
            root.Path);

        var exception = Assert.Throws<PluginLoadException>(() => CreateLoader().Load(descriptor));

        Assert.Contains("no executable assembly", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAssemblyFile_Throws()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub");
        var descriptor = StubPluginFixture.CreateDescriptor(folder);
        File.Delete(Path.Combine(folder, StubPluginFixture.AssemblyFileName));

        var exception = Assert.Throws<PluginLoadException>(() => CreateLoader().Load(descriptor));

        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptAssembly_Throws()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub");
        File.WriteAllText(Path.Combine(folder, StubPluginFixture.AssemblyFileName), "not an assembly");

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("could not be loaded", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownType_Throws()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", type: "FlushedYo.Tests.StubPlugin.DoesNotExist");

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("was not found in assembly", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FlushedYo.Tests.StubPlugin.NotAPlugin")]
    [InlineData("FlushedYo.Tests.StubPlugin.AbstractPlugin")]
    public void TypeThatIsNotAConcretePlugin_Throws(string type)
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", type: type);

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("is not a public, concrete IProviderPlugin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeWithoutContextConstructor_Throws()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", type: "FlushedYo.Tests.StubPlugin.NoContextConstructorPlugin");

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("needs a public constructor taking a PluginContext", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowingConstructor_IsReportedByTypeNameOnly()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", type: "FlushedYo.Tests.StubPlugin.ThrowingConstructorPlugin");

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("failed to initialize (InvalidOperationException)", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-IN-MESSAGE", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-IN-MESSAGE", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportedNameDifferingFromManifest_Throws()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", type: "FlushedYo.Tests.StubPlugin.MismatchedNamePlugin");

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("does not match its manifest", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportedVersionDifferingFromManifest_Throws()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", """{ "reportVersion": "9.9.9" }""");

        var exception = Assert.Throws<PluginLoadException>(() =>
            CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)));

        Assert.Contains("does not match its manifest", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyPathEscapingThePluginFolder_Throws()
    {
        using var root = new TempRoot();
        var folder = Directory.CreateDirectory(Path.Combine(root.Path, "stub")).FullName;
        var descriptor = new PluginDescriptor(
            new PluginManifest { Name = "stub", Version = "1.0.0", Assembly = "../outside.dll", Type = "A.B" },
            PluginConfiguration.Default,
            folder);

        var exception = Assert.Throws<PluginLoadException>(() => CreateLoader().Load(descriptor));

        Assert.Contains("escapes the plugin folder", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_DisposesThePlugin()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", """{ "disposeMarker": "disposed.txt" }""");
        var marker = Path.Combine(folder, "disposed.txt");

        await using (var loaded = CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder)))
        {
            Assert.False(File.Exists(marker));
        }

        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task DisposeAsyncTwice_DoesNotThrow()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub");
        var loaded = CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder));

        await loaded.DisposeAsync();
        await loaded.DisposeAsync();
    }

    [Fact]
    public async Task TwoLoadsOfTheSamePlugin_WorkIndependently()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", StubJson());
        var descriptor = StubPluginFixture.CreateDescriptor(folder);
        var loader = CreateLoader();

        await using var first = loader.Load(descriptor);
        await using var second = loader.Load(descriptor);

        Assert.NotSame(first.Plugin, second.Plugin);
        Assert.Single(await first.Plugin.DownloadTransactionsAsync(Token));
        Assert.Single(await second.Plugin.DownloadTransactionsAsync(Token));
    }

    [Fact]
    public async Task CancelledToken_SurfacesFromThePlugin()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", StubJson());
        await using var loaded = CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loaded.Plugin.DownloadTransactionsAsync(cts.Token));
    }

    [Fact]
    public async Task RuntimeFailureInThePlugin_IsNotWrappedByTheLoader()
    {
        using var root = new TempRoot();
        var folder = StubPluginFixture.CreatePlugin(root.Path, "stub", """{ "throw": "boom" }""");
        await using var loaded = CreateLoader().Load(StubPluginFixture.CreateDescriptor(folder));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => loaded.Plugin.DownloadTransactionsAsync(Token));

        Assert.Equal("boom", exception.Message);
    }

    private static string StubJson(string? secretKey = null) =>
        $$"""
        {
          "transactions": [
            { "institution": "fidelity", "externalId": "abc-123", "amount": -12.30, "date": "2026-01-02", "payee": "Corner Store", "type": "debit" }
          ]{{(secretKey is null ? "" : $", \"secretKey\": \"{secretKey}\"")}}
        }
        """;

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flushedyo-stub-plugins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class SetSecretStore(IEnumerable<string> keys) : ISecretStore
    {
        private readonly HashSet<string> _keys = [.. keys];

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.Contains(key) ? SecretValue : null);

        public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.Contains(key));
    }
}
