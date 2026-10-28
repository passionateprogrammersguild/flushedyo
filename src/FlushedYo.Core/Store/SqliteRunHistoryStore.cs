using System.Globalization;

namespace FlushedYo.Core.Store;

/// <summary>
/// <see cref="IRunHistoryStore"/> backed by the SQLite ledger. Every call opens and disposes its own
/// connection. All SQL is parameterized. The store never holds transaction values: only counts.
/// </summary>
public sealed class SqliteRunHistoryStore(LedgerDatabase database) : IRunHistoryStore
{
    private const int MaxErrorLength = 500;
    private const string SucceededLiteral = "succeeded";
    private const string FailedLiteral = "failed";

    /// <inheritdoc />
    public async Task RecordRunAsync(DownloadRun run, CancellationToken cancellationToken = default)
    {
        Validate(run);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO download_runs
                (plugin, plugin_version, started_at, finished_at, outcome, inserted, skipped, rejected, error)
            VALUES
                ($plugin, $pluginVersion, $startedAt, $finishedAt, $outcome, $inserted, $skipped, $rejected, $error);
            """;
        command.Parameters.AddWithValue("$plugin", run.Plugin);
        command.Parameters.AddWithValue("$pluginVersion", run.PluginVersion);
        command.Parameters.AddWithValue("$startedAt", FormatTimestamp(run.StartedAt));
        command.Parameters.AddWithValue("$finishedAt", FormatTimestamp(run.FinishedAt));
        command.Parameters.AddWithValue("$outcome", run.Outcome == DownloadOutcome.Succeeded ? SucceededLiteral : FailedLiteral);
        command.Parameters.AddWithValue("$inserted", run.Inserted);
        command.Parameters.AddWithValue("$skipped", run.Skipped);
        command.Parameters.AddWithValue("$rejected", run.Rejected);
        command.Parameters.AddWithValue("$error", run.Error is null ? DBNull.Value : Truncate(run.Error));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DownloadRun>> GetLastRunsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runs = new Dictionary<string, DownloadRun>(StringComparer.OrdinalIgnoreCase);
        if (!database.Exists)
        {
            return runs;
        }

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Ordered by id so that, if two rows differ only by plugin-name case, the newest wins the dictionary slot.
        command.CommandText =
            """
            SELECT plugin, plugin_version, started_at, finished_at, outcome, inserted, skipped, rejected, error
            FROM download_runs
            WHERE id IN (SELECT MAX(id) FROM download_runs GROUP BY plugin)
            ORDER BY id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var plugin = reader.GetString(0);
            runs[plugin] = new DownloadRun(
                plugin,
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                ParseTimestamp(reader.GetString(3)),
                ParseOutcome(reader.GetString(4)),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8));
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> GetStoredTransactionCountsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (!database.Exists)
        {
            return counts;
        }

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT lower(institution), COUNT(*) FROM transactions GROUP BY lower(institution);";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    private static void Validate(DownloadRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (string.IsNullOrWhiteSpace(run.Plugin))
        {
            throw new ArgumentException("The run's plugin name is required.", nameof(run));
        }

        if (string.IsNullOrWhiteSpace(run.PluginVersion))
        {
            throw new ArgumentException("The run's plugin version is required.", nameof(run));
        }

        if (run.Inserted < 0 || run.Skipped < 0 || run.Rejected < 0)
        {
            throw new ArgumentException("The run's counts must not be negative.", nameof(run));
        }

        if (run.FinishedAt < run.StartedAt)
        {
            throw new ArgumentException("The run cannot finish before it started.", nameof(run));
        }

        if (!Enum.IsDefined(run.Outcome))
        {
            throw new ArgumentException("The run's outcome is not a defined value.", nameof(run));
        }

        if (run.Outcome == DownloadOutcome.Failed && string.IsNullOrWhiteSpace(run.Error))
        {
            throw new ArgumentException("A failed run requires an error message.", nameof(run));
        }

        if (run.Outcome == DownloadOutcome.Succeeded && run.Error is not null)
        {
            throw new ArgumentException("A succeeded run must not carry an error message.", nameof(run));
        }
    }

    private static string Truncate(string error) =>
        error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DownloadOutcome ParseOutcome(string value) => value switch
    {
        SucceededLiteral => DownloadOutcome.Succeeded,
        FailedLiteral => DownloadOutcome.Failed,
        _ => throw new LedgerException("The ledger contains a download run with an unknown outcome."),
    };
}
