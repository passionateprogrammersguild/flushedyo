using System.Text;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Reads a downloaded statement artifact as text for the CSV adapter. The artifact stays in the
/// coordinator-owned directory; the reader only opens it, never moves or deletes it, so a durable
/// import can still complete. Diagnostics are value-free: they name the broken invariant, never a
/// row value, path, account, or hash.
/// </summary>
public static class FidelityArtifactReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads and decodes one artifact inside the byte cap.</summary>
    /// <param name="path">Absolute path of the artifact produced by the typed download operation.</param>
    /// <param name="maxBytes">Largest artifact the caller accepts, in bytes.</param>
    /// <returns>The decoded CSV text.</returns>
    /// <exception cref="FidelityAdapterException">
    /// The artifact is missing, empty, larger than the cap, not regular UTF-8, or unreadable.
    /// </exception>
    public static string Read(string path, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "The artifact byte cap must be positive.");
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FidelityAdapterException("The downloaded artifact does not exist.");
        }

        if (info.Length == 0)
        {
            throw new FidelityAdapterException("The downloaded artifact is empty.");
        }

        if (info.Length > maxBytes)
        {
            throw new FidelityAdapterException("The downloaded artifact exceeds the configured byte cap.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FidelityAdapterException("The downloaded artifact could not be read.");
        }

        if (bytes.LongLength > maxBytes)
        {
            throw new FidelityAdapterException("The downloaded artifact exceeds the configured byte cap.");
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new FidelityAdapterException("The downloaded artifact is not valid UTF-8 text.");
        }
    }
}
