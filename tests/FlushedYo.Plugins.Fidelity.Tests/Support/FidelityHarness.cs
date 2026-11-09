using FlushedYo.Core.Configuration;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace FlushedYo.Plugins.Fidelity.Tests.Support;

/// <summary>
/// Wires a <see cref="FidelityProviderPlugin"/> over a temp-directory SQLite ledger and a fake
/// statement source, so the whole download → adapt → import workflow runs hermetically.
/// </summary>
internal sealed class FidelityHarness : IAsyncDisposable
{
    private readonly string _root;

    public FidelityHarness()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "flushedyo-fidelity-" + Guid.NewGuid().ToString("N"));
        ArtifactDirectory = Path.Combine(_root, "artifacts");
        Directory.CreateDirectory(ArtifactDirectory);
        LedgerPath = Path.Combine(_root, "ledger", "flushedyo.db");
        Store = new SqliteAccountLedgerStore(
            new LedgerDatabase(
                Microsoft.Extensions.Options.Options.Create(new FlushedYoOptions { LedgerPath = LedgerPath }),
                NullLogger<LedgerDatabase>.Instance));
    }

    public string ArtifactDirectory { get; }

    public string LedgerPath { get; }

    public SqliteAccountLedgerStore Store { get; }

    public FakeLogger<FidelityProviderPlugin> Logger { get; } = new();

    public static FidelityProviderOptions ProviderOptions(params string[] accountKeys) => new()
    {
        ArtifactDirectory = string.Empty,
        MaxArtifactBytes = 1_000_000,
        AccountKeys = [.. accountKeys],
    };

    public FidelityProviderPlugin CreatePlugin(
        IFidelityStatementSource source,
        FidelityProviderOptions? options = null)
    {
        var effective = options ?? ProviderOptions();
        effective.ArtifactDirectory = string.IsNullOrEmpty(effective.ArtifactDirectory)
            ? ArtifactDirectory
            : effective.ArtifactDirectory;

        var importer = new AccountTransactionImporter(
            Store,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<AccountTransactionImporter>.Instance);

        return new FidelityProviderPlugin(source, effective, importer, Logger);
    }

    /// <summary>Builds a source whose fake exports live in the harness artifact directory.</summary>
    public FakeFidelityStatementSource Source(Dictionary<string, string> exports) =>
        new(ArtifactDirectory, exports);

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A lingering SQLite handle on a shared runner must not fail a test.
        }

        return ValueTask.CompletedTask;
    }

    internal static FlushedYoOptions LedgerOptions(string ledgerPath) =>
        new() { LedgerPath = ledgerPath };
}
