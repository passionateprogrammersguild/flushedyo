namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Pure artifact policy: where a suggested browser filename may land inside the caller-owned
/// directory, and whether a byte count satisfies a cap. It never reads or holds file content, and it
/// refuses any name that could escape the directory.
/// </summary>
internal static class DownloadArtifactPolicy
{
    internal const string FallbackFileName = "download.bin";
    private const int MaximumFileNameLength = 128;
    private static readonly HashSet<char> ForbiddenFileNameCharacters = new()
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
        ':', '*', '?', '"', '<', '>', '|'
    };

    /// <summary>
    /// Resolves the artifact path for a browser-suggested filename. Directory components, traversal
    /// segments, control characters, and over-long names are reduced to a single safe file name.
    /// </summary>
    internal static string ResolvePath(string directory, string? suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var safeName = SanitizeFileName(suggestedFileName);
        var candidate = Path.GetFullPath(Path.Combine(directory, safeName));
        var normalizedDirectory = Path.GetFullPath(directory);

        if (!string.Equals(Path.GetDirectoryName(candidate), normalizedDirectory, StringComparison.Ordinal))
        {
            throw new BridgeOperationException("The suggested download file name escapes the caller directory.");
        }

        return candidate;
    }

    /// <summary>Throws a value-free bridge failure when an artifact exceeds the caller's byte cap.</summary>
    internal static void EnsureWithinCap(long sizeBytes, long maxBytes)
    {
        if (sizeBytes > maxBytes)
        {
            throw new BridgeOperationException("The downloaded artifact exceeded the caller's size cap.");
        }
    }

    private static string SanitizeFileName(string? suggestedFileName)
    {
        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            return FallbackFileName;
        }

        var leaf = Path.GetFileName(suggestedFileName);
        var cleaned = new string(leaf.Where(IsPermittedFileNameCharacter).ToArray()).Trim('.', ' ');
        if (cleaned.Length == 0 || cleaned is "." or "..")
        {
            return FallbackFileName;
        }

        return cleaned.Length > MaximumFileNameLength ? cleaned[..MaximumFileNameLength] : cleaned;
    }

    private static bool IsPermittedFileNameCharacter(char character) =>
        !char.IsControl(character) && !ForbiddenFileNameCharacters.Contains(character);
}
