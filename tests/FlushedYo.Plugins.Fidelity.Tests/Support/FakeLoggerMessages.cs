using Microsoft.Extensions.Logging.Testing;

namespace FlushedYo.Plugins.Fidelity.Tests.Support;

/// <summary>Renders the captured records of a <see cref="FakeLogger{T}"/> for leak assertions.</summary>
internal static class FakeLoggerMessages
{
    /// <summary>Returns every rendered message, in the order it was logged.</summary>
    internal static IReadOnlyList<string> Messages<T>(this FakeLogger<T> logger) =>
        [.. logger.Collector.GetSnapshot().Select(record => record.Message)];
}
