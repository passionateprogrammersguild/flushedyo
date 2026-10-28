namespace FlushedYo.Core.Tests.Plugin;

/// <summary>A throwaway plugin directory under the system temp path, deleted on dispose.</summary>
internal sealed class TempPluginDirectory : IDisposable
{
    public TempPluginDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "flushedyo-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddPlugin(
        string folderName,
        string? manifestJson,
        string? configJson = null,
        IEnumerable<string>? skillFiles = null,
        IEnumerable<string>? toolDirectories = null,
        IEnumerable<string>? assemblyFiles = null)
    {
        var folder = Path.Combine(Root, folderName);
        Directory.CreateDirectory(folder);

        if (manifestJson is not null)
        {
            File.WriteAllText(Path.Combine(folder, "plugin.json"), manifestJson);
        }

        if (configJson is not null)
        {
            File.WriteAllText(Path.Combine(folder, "config.json"), configJson);
        }

        foreach (var skillFile in skillFiles ?? [])
        {
            var path = Path.Combine(folder, "skills", skillFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# skill");
        }

        foreach (var toolDirectory in toolDirectories ?? [])
        {
            Directory.CreateDirectory(Path.Combine(folder, "tools", toolDirectory));
        }

        foreach (var assemblyFile in assemblyFiles ?? [])
        {
            var path = Path.Combine(folder, assemblyFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }

        return folder;
    }

    public static string Manifest(string name, string version = "1.0.0", string extra = "") =>
        $$"""{ "name": "{{name}}", "version": "{{version}}"{{extra}} }""";

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
