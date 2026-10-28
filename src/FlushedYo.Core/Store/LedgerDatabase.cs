using FlushedYo.Core.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlushedYo.Core.Store;

/// <summary>
/// Opens connections to the local SQLite ledger. Nothing touches the disk until the first
/// <see cref="OpenAsync"/>, which creates the directory and database file and applies migrations.
/// </summary>
public sealed partial class LedgerDatabase
{
    private const string InMemoryPath = ":memory:";

    private readonly Lazy<string> _databasePath;
    private readonly ILogger<LedgerDatabase> _logger;
    private readonly Lock _migrationGate = new();
    private bool _migrated;

    /// <summary>Creates a ledger whose path comes from <see cref="FlushedYoOptions.LedgerPath"/>.</summary>
    /// <param name="options">Application options; the path is resolved on first use.</param>
    /// <param name="logger">Logger.</param>
    public LedgerDatabase(IOptions<FlushedYoOptions> options, ILogger<LedgerDatabase> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _databasePath = new Lazy<string>(() => RepositoryPathResolver.Resolve(options.Value.LedgerPath));
        _logger = logger;
    }

    internal LedgerDatabase(string databasePath, ILogger<LedgerDatabase> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.Equals(databasePath.Trim(), InMemoryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "In-memory ledgers are not supported: each unpooled connection would see its own empty database.",
                nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        _databasePath = new Lazy<string>(fullPath);
        _logger = logger;
    }

    /// <summary>True when the ledger file already exists. Never creates anything.</summary>
    public bool Exists => File.Exists(_databasePath.Value);

    /// <summary>
    /// Opens a connection to the ledger, creating and migrating the database on first use.
    /// The caller owns the returned connection and must dispose it.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels opening the connection.</param>
    /// <returns>An open connection.</returns>
    /// <exception cref="LedgerException">The schema is newer than this build supports, or a migration failed.</exception>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = _databasePath.Value;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        LogOpening(_logger, path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            EnsureMigrated(connection);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void EnsureMigrated(SqliteConnection connection)
    {
        lock (_migrationGate)
        {
            if (_migrated)
            {
                return;
            }

            LedgerMigrations.Apply(connection);
            _migrated = true;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opening ledger at '{Path}'.")]
    private static partial void LogOpening(ILogger logger, string path);
}
