using System.Reflection;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Plugin;

/// <summary>Turns a discovered plugin folder into a running <see cref="IProviderPlugin"/>.</summary>
public interface IPluginAssemblyLoader
{
    /// <summary>Loads and constructs the plugin described by <paramref name="descriptor"/>.</summary>
    /// <param name="descriptor">A discovered plugin that declares an assembly and type.</param>
    /// <returns>The running plugin. The caller disposes it to unload the plugin's assembly.</returns>
    /// <exception cref="PluginLoadException">The plugin cannot be loaded or does not match its manifest.</exception>
    LoadedPlugin Load(PluginDescriptor descriptor);
}

/// <summary>
/// Loads a plugin's assembly into an isolated, collectible load context, constructs the declared type
/// with a <see cref="PluginContext"/>, and verifies it matches the manifest. Keeps no shared mutable
/// state, so concurrent calls are safe.
/// </summary>
public sealed class PluginAssemblyLoader(ISecretStore secretStore, ILoggerFactory loggerFactory) : IPluginAssemblyLoader
{
    /// <inheritdoc />
    public LoadedPlugin Load(PluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var name = descriptor.Name;
        var manifest = descriptor.Manifest;
        if (manifest.Assembly is null || manifest.Type is null)
        {
            throw new PluginLoadException($"Plugin '{name}' declares no executable assembly.");
        }

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(descriptor.DirectoryPath));
        var assemblyPath = Path.GetFullPath(Path.Combine(folder, manifest.Assembly));
        if (!assemblyPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new PluginLoadException($"Plugin '{name}' assembly path escapes the plugin folder.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new PluginLoadException($"Plugin assembly '{manifest.Assembly}' for '{name}' was not found.");
        }

        var context = new PluginLoadContext(assemblyPath);
        var succeeded = false;
        try
        {
            var plugin = Construct(context, assemblyPath, descriptor, manifest.Assembly, manifest.Type);
            succeeded = true;
            return new LoadedPlugin(plugin, context);
        }
        finally
        {
            if (!succeeded)
            {
                context.Unload();
            }
        }
    }

    private IProviderPlugin Construct(
        PluginLoadContext context,
        string assemblyPath,
        PluginDescriptor descriptor,
        string assemblyName,
        string typeName)
    {
        var name = descriptor.Name;

        Type? type;
        try
        {
            type = context.LoadFromAssemblyPath(assemblyPath).GetType(typeName, throwOnError: false);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or TypeLoadException)
        {
            throw new PluginLoadException(
                $"Plugin '{name}' assembly '{assemblyName}' could not be loaded ({ex.GetType().Name}).",
                ex);
        }

        if (type is null)
        {
            throw new PluginLoadException($"Type '{typeName}' was not found in assembly '{assemblyName}'.");
        }

        if (!type.IsPublic || type.IsAbstract || type.IsGenericTypeDefinition || !typeof(IProviderPlugin).IsAssignableFrom(type))
        {
            throw new PluginLoadException($"Type '{typeName}' is not a public, concrete IProviderPlugin.");
        }

        var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, [typeof(PluginContext)], modifiers: null)
            ?? throw new PluginLoadException($"Type '{typeName}' needs a public constructor taking a PluginContext.");

        var pluginContext = new PluginContext(descriptor, secretStore, loggerFactory.CreateLogger("plugin." + name));

        IProviderPlugin plugin;
        try
        {
            plugin = (IProviderPlugin)constructor.Invoke([pluginContext]);
        }
        catch (TargetInvocationException ex)
        {
            // The plugin's own exception is deliberately not attached: its message could contain a secret,
            // and any handler that prints the exception chain would leak it.
            throw new PluginLoadException($"Plugin '{name}' failed to initialize ({ex.InnerException?.GetType().Name ?? "unknown"}).");
        }

        if (!string.Equals(plugin.Name, name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plugin.Version, descriptor.Version, StringComparison.Ordinal))
        {
            DisposeQuietly(plugin);
            throw new PluginLoadException($"Plugin '{name}' reports a name or version that does not match its manifest.");
        }

        return plugin;
    }

    private static void DisposeQuietly(IProviderPlugin plugin)
    {
        try
        {
            if (plugin is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (plugin is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception)
        {
            // The load failure being reported matters more than a failure to dispose the rejected instance.
        }
    }
}
