using FlushedYo.Core.Models;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// Contract implemented by every financial-institution plugin (plan §4).
/// Implementations download raw data from the institution and map it to the canonical
/// <see cref="Transaction"/> and <see cref="Holding"/> models. They must honor the
/// supplied cancellation token and must never log or persist secret values.
/// </summary>
public interface IProviderPlugin
{
    /// <summary>Institution key for the plugin, for example <c>fidelity</c>.</summary>
    string Name { get; }

    /// <summary>Plugin version, for example <c>1.0.0</c>.</summary>
    string Version { get; }

    /// <summary>Downloads the institution's transactions.</summary>
    /// <param name="cancellationToken">Token that cancels the download.</param>
    /// <returns>The downloaded transactions, unnormalized and undeduplicated.</returns>
    Task<IEnumerable<Transaction>> DownloadTransactionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the institution's holdings.</summary>
    /// <param name="cancellationToken">Token that cancels the download.</param>
    /// <returns>The downloaded holdings.</returns>
    Task<IEnumerable<Holding>> DownloadHoldingsAsync(CancellationToken cancellationToken = default);
}
