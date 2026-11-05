namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// The caller-supplied parameters for one statement download: the directory the artifact lands in,
/// the byte cap, and the bounded wait. Kept separate from <see cref="DownloadRequest"/> so the
/// bridge's public signature stays small while the driver retains full control of the claim.
/// </summary>
public readonly record struct DownloadStatementOptions(
    string DestinationDirectory,
    long MaxBytes,
    TimeSpan Timeout)
{
    /// <summary>Validates the caller's arguments and wraps them without holding any file content.</summary>
    public static DownloadStatementOptions Create(
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

        return new DownloadStatementOptions(destinationDirectory, maxBytes, timeout);
    }
}
