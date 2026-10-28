using System.Text.Json;
using FlushedYo.Core.Commands;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;
using static FlushedYo.Core.Tests.Commands.CommandTestData;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Selection, output, and exit codes for login (spec: login_credential_preflight).</summary>
public sealed class LoginCommandTests
{
    private const string SecretValue = "S3CR3T-VALUE";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeSecretStore StoreWith(params string[] keys) =>
        new(keys.ToDictionary(static key => key, static _ => SecretValue));

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        CommandLineOptions options,
        FakeSecretStore store,
        params PluginDescriptor[] plugins)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var command = new LoginCommand(new StubPluginCatalog(plugins), new LoginService(store));

        var exitCode = await command.ExecuteAsync(options, output, error, Token);

        return (exitCode, output.ToString(), error.ToString());
    }

    private static readonly CommandLineOptions Table = new(null, false);

    [Fact]
    public async Task AllOk_ReturnsZero_WithATableAndNoErrorOutput()
    {
        var (exitCode, output, error) = await RunAsync(Table, StoreWith("a/u", "a/p"), Descriptor("a", "a/u", "a/p"));

        Assert.Equal(0, exitCode);
        Assert.Contains("ok", output, StringComparison.Ordinal);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task AnyMissing_ReturnsOne_AndExplainsOnStderr()
    {
        var (exitCode, output, error) = await RunAsync(Table, StoreWith("a/u"), Descriptor("a", "a/u", "a/p"));

        Assert.Equal(1, exitCode);
        Assert.Contains("missing", output, StringComparison.Ordinal);
        Assert.Contains("Some credentials were not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonOutput_ParsesAndKeepsStderrEmpty_EvenWhenSomethingIsMissing()
    {
        var (exitCode, output, error) = await RunAsync(new CommandLineOptions(null, true), StoreWith("a/u"), Descriptor("a", "a/u", "a/p"));

        Assert.Equal(1, exitCode);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task PluginFlag_ChecksOnlyThatPluginsKeys()
    {
        var store = StoreWith("a/u", "a/p", "b/u", "b/p");

        var (exitCode, _, _) = await RunAsync(new CommandLineOptions("b", false), store, Descriptor("a", "a/u", "a/p"), Descriptor("b", "b/u", "b/p"));

        Assert.Equal(0, exitCode);
        Assert.Equal(["b/u", "b/p"], store.CheckedKeys);
    }

    [Fact]
    public async Task UnknownPlugin_ReturnsOne_WithTheSelectorMessageAndNoOutput()
    {
        var (exitCode, output, error) = await RunAsync(new CommandLineOptions("nope", false), StoreWith(), Descriptor("a", "a/u", "a/p"));

        Assert.Equal(1, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Plugin 'nope' was not found. Installed plugins: a.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoPlugins_PrintsTheMessage_AndReturnsZero()
    {
        var (exitCode, output, error) = await RunAsync(Table, StoreWith());

        Assert.Equal(0, exitCode);
        Assert.Equal("No plugins installed." + Environment.NewLine, output);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task SecretStoreException_PropagatesToTheHost()
    {
        var store = new FakeSecretStore(existsFailure: new SecretStoreException("vault is locked"));

        await Assert.ThrowsAsync<SecretStoreException>(() => RunAsync(Table, store, Descriptor("a", "a/u", "a/p")));
    }

    [Fact]
    public async Task Output_NeverContainsTheSecretValues()
    {
        var (_, output, error) = await RunAsync(Table, StoreWith("a/u", "a/p"), Descriptor("a", "a/u", "a/p"));
        var (_, jsonOutput, jsonError) = await RunAsync(new CommandLineOptions(null, true), StoreWith("a/u", "a/p"), Descriptor("a", "a/u", "a/p"));

        Assert.All(
            new[] { output, error, jsonOutput, jsonError },
            text => Assert.DoesNotContain(SecretValue, text, StringComparison.Ordinal));
    }
}
