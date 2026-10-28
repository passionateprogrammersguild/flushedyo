using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlushedYo.ArchitectureTests;

/// <summary>
/// Loads the flushedyo assemblies once and exposes the layer providers the architecture rules
/// share. The layers mirror the component map in docs/Architecture.md §4.
/// </summary>
internal static class FlushedYoArchitecture
{
    /// <summary>The FlushedYo.Core assembly (contracts and shared logic).</summary>
    public static readonly System.Reflection.Assembly CoreAssembly = typeof(FlushedYo.Core.Models.Transaction).Assembly;

    /// <summary>The FlushedYo.Cli assembly (thin composition root); resolved from the test output.</summary>
    public static readonly System.Reflection.Assembly CliAssembly = System.Reflection.Assembly.Load("FlushedYo.Cli");

    /// <summary>The imported architecture (docs/Architecture.md §2–§4).</summary>
    public static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(CoreAssembly, CliAssembly)
        .Build();

    public static readonly IObjectProvider<IType> CliLayer =
        Types().That().ResideInAssembly(CliAssembly).As("FlushedYo.Cli");

    public static readonly IObjectProvider<IType> CoreLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core").As("FlushedYo.Core");

    public static readonly IObjectProvider<IType> ModelsLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Models").As("FlushedYo.Core.Models");

    public static readonly IObjectProvider<IType> ConfigurationLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Configuration").As("FlushedYo.Core.Configuration");

    public static readonly IObjectProvider<IType> SecretStoreLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.SecretStore").As("FlushedYo.Core.SecretStore");

    public static readonly IObjectProvider<IType> PluginLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Plugin").As("FlushedYo.Core.Plugin");

    public static readonly IObjectProvider<IType> CommandsLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Commands").As("FlushedYo.Core.Commands");

    public static readonly IObjectProvider<IType> TrackingLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Tracking").As("FlushedYo.Core.Tracking");

    public static readonly IObjectProvider<IType> StoreLayer =
        Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Store").As("FlushedYo.Core.Store");

    /// <summary>Combines layer providers into one forbidden set for <c>NotDependOnAny</c>.</summary>
    public static IObjectProvider<IType> Union(params IObjectProvider<IType>[] layers)
    {
        ArgumentOutOfRangeException.ThrowIfZero(layers.Length);
        var given = Types().That().Are(layers[0]);
        for (var i = 1; i < layers.Length; i++)
        {
            given = given.Or().Are(layers[i]);
        }

        return given.As(string.Join(" or ", layers.Select(static layer => layer.Description)));
    }
}
