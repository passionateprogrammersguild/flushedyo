using FlushedYo.Core.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.Core.Tests.Store;

/// <summary>
/// A throwaway ledger under the system temp path. The database file lives in a nested
/// <c>data</c> folder so directory creation is exercised. Deleted on dispose.
/// </summary>
internal sealed class TempLedger : IDisposable
{
    public TempLedger()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "flushedyo-ledger-" + Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DirectoryPath, "data", "ledger.db");
        Database = new LedgerDatabase(DatabasePath, NullLogger<LedgerDatabase>.Instance);
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public LedgerDatabase Database { get; }

    /// <summary>
    /// Opens a connection that bypasses <see cref="LedgerDatabase"/> (no migrations), creating the
    /// directory if needed. The caller disposes it.
    /// </summary>
    public SqliteConnection OpenRawConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
