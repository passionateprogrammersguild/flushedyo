namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Copies a browser download stream into a caller-owned file while enforcing a hard byte cap. The
/// copy is incremental, so the cap is reached before more bytes land than the caller allowed. On
/// any failure — cap, cancellation, or I/O — the partial file is deleted before the error surfaces.
/// </summary>
internal static class ArtifactCopy
{
    private const int BufferSize = 81920;

    internal static async Task<long> CopyAsync(
        Stream source,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var buffer = new byte[BufferSize];
        long total = 0;
        try
        {
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                DownloadArtifactPolicy.EnsureWithinCap(total, maxBytes);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return total;
        }
        catch (Exception)
        {
            Delete(destinationPath);
            throw;
        }
    }

    internal static void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A failed cleanup is reported by the caller's fault; browser diagnostics never escape.
        }
    }

    internal static void DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // A failed scratch cleanup never widens the operation; the session is already at rest.
        }
    }
}
