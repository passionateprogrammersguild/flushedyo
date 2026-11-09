using System.Text;
using FlushedYo.Plugins.Fidelity;
using FlushedYo.Plugins.Fidelity.Tests.Support;

namespace FlushedYo.Plugins.Fidelity.Tests;

public sealed class FidelityArtifactReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "flushedyo-fidelity-reader-" + Guid.NewGuid().ToString("N"));

    public FidelityArtifactReaderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReadsUtf8Csv()
    {
        var path = Write("export.csv", Encoding.UTF8.GetBytes(FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "-1.00"))));

        var text = FidelityArtifactReader.Read(path, 1_000);

        Assert.Contains("Run Date", text, StringComparison.Ordinal);
        Assert.True(File.Exists(path), "Reading must not consume the artifact before its import completes.");
    }

    [Fact]
    public void RejectsMissingEmptyOversizedAndNonUtf8Artifacts()
    {
        Assert.Throws<FidelityAdapterException>(
            () => FidelityArtifactReader.Read(Path.Combine(_directory, "missing.csv"), 1_000));

        var empty = Write("empty.csv", []);
        Assert.Throws<FidelityAdapterException>(() => FidelityArtifactReader.Read(empty, 1_000));

        var large = Write("large.csv", Encoding.UTF8.GetBytes(new string('a', 64)));
        Assert.Throws<FidelityAdapterException>(() => FidelityArtifactReader.Read(large, 8));

        var binary = Write("binary.csv", [0xFF, 0xFE, 0xFD]);
        Assert.Throws<FidelityAdapterException>(() => FidelityArtifactReader.Read(binary, 1_000));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
