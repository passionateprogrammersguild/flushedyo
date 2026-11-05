namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Validation-free configuration for one provider download: the caller-owned directory to write
/// into, the byte cap, and the bounded wait. Kept separate from <see cref="DownloadRequest"/> so the
/// request can carry the trigger selector and verified snapshot alongside an options value.
/// </summary>
internal readonly record struct DownloadOptions(
    string DestinationDirectory,
    long MaxBytes,
    TimeSpan Timeout)
{
    internal static DownloadOptions Create(
        string destinationDirectory,
        long maxBytes,
        TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !System.IO.Path.IsPathFullyQualified(destinationDirectory))
        {
            throw new ArgumentException(
                "A fully-qualified caller-owned download directory is required.",
                nameof(destinationDirectory));
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "The download size cap must be positive.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The download wait must be positive.");
        }

        return new DownloadOptions(destinationDirectory, maxBytes, timeout);
    }
}
