using FlushedYo.Core.Models;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.SecretStore;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Commands;

/// <summary>
/// Runs plugins one at a time, feeds their transactions through the dedup tracker, and records one run
/// row per attempted plugin. A failing plugin never stops the others.
/// </summary>
/// <remarks>
/// Only <see cref="PluginLoadException"/> and <see cref="SecretStoreException"/> messages are reported, because
/// this codebase controls their text; for any other exception only the type name is reported, since third-party
/// plugin messages could contain secrets. Nothing the plugin returned (payees, amounts, IDs) is ever logged.
/// </remarks>
public sealed partial class DownloadService(
    IPluginAssemblyLoader loader,
    TransactionTracker tracker,
    IRunHistoryStore history,
    TimeProvider timeProvider,
    ILogger<DownloadService> logger)
{
    private const int MaxErrorLength = 500;

    /// <summary>Explains why <paramref name="plugin"/> cannot be downloaded, or returns <see langword="null"/> when it can.</summary>
    /// <param name="plugin">The discovered plugin.</param>
    /// <returns>The first reason found, or <see langword="null"/> when the plugin is runnable.</returns>
    public static string? GetNotRunnableReason(PluginDescriptor plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (plugin.Manifest.Assembly is null || plugin.Manifest.Type is null)
        {
            return "declares no executable assembly";
        }

        return plugin.Capabilities.Contains(PluginCapability.DownloadTransactions)
            ? null
            : "does not declare the download-transactions capability";
    }

    /// <summary>Downloads <paramref name="plugins"/> in the order given.</summary>
    /// <param name="plugins">The plugins to run.</param>
    /// <param name="cancellationToken">Token that cancels the command; a cancelled plugin leaves no run row.</param>
    /// <returns>One result per plugin.</returns>
    /// <remarks>
    /// Ledger and tracker errors are command failures, not plugin failures, and propagate. If the tracker throws
    /// after inserting some rows, no run row is written for that plugin; re-running is safe because inserts are idempotent.
    /// </remarks>
    public async Task<DownloadReport> RunAsync(IReadOnlyList<PluginDescriptor> plugins, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var results = new List<PluginDownloadResult>(plugins.Count);
        foreach (var descriptor in plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunPluginAsync(descriptor, cancellationToken).ConfigureAwait(false));
        }

        return new DownloadReport(results);
    }

    private async Task<PluginDownloadResult> RunPluginAsync(PluginDescriptor descriptor, CancellationToken cancellationToken)
    {
        var name = descriptor.Name;

        var notRunnable = GetNotRunnableReason(descriptor);
        if (notRunnable is not null)
        {
            return new PluginDownloadResult(name, DownloadStatus.NotRunnable, 0, 0, 0, notRunnable);
        }

        var startedAt = timeProvider.GetUtcNow();
        var version = descriptor.Version;
        List<Transaction>? downloaded = null;
        string? failure = null;

        try
        {
            await using var loaded = loader.Load(descriptor);
            version = loaded.Plugin.Version;

            // Materialize here so a plugin iterator that throws mid-enumeration is a plugin failure, and
            // nothing from the plugin's assembly is touched after its load context is released.
            downloaded = (await loaded.Plugin.DownloadTransactionsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failure = Describe(ex);
            LogPluginFailed(logger, name, ex.GetType().Name);
        }

        if (failure is not null)
        {
            await history.RecordRunAsync(
                new DownloadRun(name, version, startedAt, timeProvider.GetUtcNow(), DownloadOutcome.Failed, 0, 0, 0, failure),
                cancellationToken).ConfigureAwait(false);
            return new PluginDownloadResult(name, DownloadStatus.Failed, 0, 0, 0, failure);
        }

        var valid = new List<Transaction>(downloaded!.Count);
        var mismatched = 0;
        foreach (var transaction in downloaded)
        {
            // Null elements go to the tracker, which rejects them.
            if (transaction is not null && !string.Equals(transaction.Institution, name, StringComparison.OrdinalIgnoreCase))
            {
                mismatched++;
            }
            else
            {
                valid.Add(transaction!);
            }
        }

        if (mismatched > 0)
        {
            LogMismatchedInstitution(logger, name, mismatched);
        }

        var tracked = await tracker.TrackAsync(valid, version, cancellationToken).ConfigureAwait(false);
        var rejected = tracked.Rejected + mismatched;

        await history.RecordRunAsync(
            new DownloadRun(name, version, startedAt, timeProvider.GetUtcNow(), DownloadOutcome.Succeeded, tracked.Inserted, tracked.Skipped, rejected, null),
            cancellationToken).ConfigureAwait(false);

        return new PluginDownloadResult(name, DownloadStatus.Succeeded, tracked.Inserted, tracked.Skipped, rejected, null);
    }

    private static string Describe(Exception exception)
    {
        var message = exception is PluginLoadException or SecretStoreException
            ? exception.Message
            : $"Plugin threw {exception.GetType().Name}.";

        return message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Plugin '{Plugin}' failed ({ExceptionType}).")]
    private static partial void LogPluginFailed(ILogger logger, string plugin, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Plugin '{Plugin}' returned {Count} transactions for a different institution; they were rejected.")]
    private static partial void LogMismatchedInstitution(ILogger logger, string plugin, int count);
}
