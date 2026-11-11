using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace FlushedYo.Core.Plugin;

/// <summary>
/// A collectible load context for one plugin. <c>FlushedYo.Core</c> and <c>Microsoft.Extensions.*</c>
/// always resolve from the host so <see cref="IProviderPlugin"/>, <c>ISecretStore</c>, and
/// <c>ILogger</c> have a single identity on both sides; everything else resolves from the plugin folder.
/// </summary>
internal sealed class PluginLoadContext(string assemblyPath)
    : AssemblyLoadContext("flushedyo-plugin:" + Path.GetFileName(assemblyPath), isCollectible: true)
{
    private const string CoreAssemblyName = "FlushedYo.Core";
    private const string SharedPrefix = "Microsoft.Extensions.";

    private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsHostSharedAssembly(assemblyName.Name))
        {
            return null;
        }

        return ResolvePath(_resolver.ResolveAssemblyToPath(assemblyName));
    }

    private static bool IsHostSharedAssembly(string? name) =>
        name is CoreAssemblyName || (name is not null && name.StartsWith(SharedPrefix, StringComparison.Ordinal));

    private Assembly? ResolvePath(string? path) => path is null ? null : LoadFromAssemblyPath(path);

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
