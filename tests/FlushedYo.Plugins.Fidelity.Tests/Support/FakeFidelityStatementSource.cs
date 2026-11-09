namespace FlushedYo.Plugins.Fidelity.Tests.Support;

/// <summary>
/// A hermetic <see cref="IFidelityStatementSource"/>: writes synthetic CSV text into a caller-owned
/// temp directory instead of touching a browser, and records the call order so the workflow contract
/// (open → login → download) is asserted without credentials or network access.
/// </summary>
internal sealed class FakeFidelityStatementSource : IFidelityStatementSource
{
    private readonly Dictionary<string, string> _exports;
    private readonly Dictionary<string, Exception> _failures;
    private readonly string _directory;
    private readonly List<string> _calls = [];

    public FakeFidelityStatementSource(string directory, Dictionary<string, string> exports)
        : this(directory, exports, [])
    {
    }

    public FakeFidelityStatementSource(
        string directory,
        Dictionary<string, string> exports,
        Dictionary<string, Exception> failures)
    {
        _directory = directory;
        _exports = exports;
        _failures = failures;
    }

    public IReadOnlyList<string> Calls => _calls;

    public List<string> ReportedAccounts { get; init; } = [];

    public bool DisposeCalled { get; private set; }

    public Task OpenAsync(Uri loginUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("open");
        return Task.CompletedTask;
    }

    public Task LoginAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("login");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("list");
        return Task.FromResult<IReadOnlyList<string>>(ReportedAccounts);
    }

    public Task<StatementArtifact> DownloadStatementAsync(
        string accountKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("download");

        if (_failures.TryGetValue(accountKey, out var failure))
        {
            throw failure;
        }

        var path = Path.Combine(_directory, accountKey + ".csv");
        File.WriteAllText(path, _exports[accountKey]);
        return Task.FromResult(new StatementArtifact(path, accountKey, null!));
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A fixed clock so import timestamps never depend on wall time.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
