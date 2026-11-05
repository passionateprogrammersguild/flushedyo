namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// A downloaded artifact that landed in a caller-owned directory. It carries only structured
/// metadata: the path, byte length, and the verified origin that produced it. File contents never
/// enter this type, so no download value can leak into logs or model transcripts.
/// </summary>
public sealed record BrowserArtifact
{
    internal BrowserArtifact(string path, long sizeBytes, string origin)
    {
        Path = path;
        SizeBytes = sizeBytes;
        Origin = origin;
    }

    /// <summary>Gets the absolute path of the artifact inside the caller-owned directory.</summary>
    public string Path { get; }

    /// <summary>Gets the artifact length in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Gets the verified origin that served the artifact.</summary>
    public string Origin { get; }
}
