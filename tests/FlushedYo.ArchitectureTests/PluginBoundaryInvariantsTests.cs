using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using FlushedYo.Core.Plugin;
using FlushedYo.Core.Store;
using FlushedYo.Core.Tracking;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlushedYo.ArchitectureTests;

/// <summary>
/// Pins the extensibility and idempotency invariants from docs/Architecture.md §3, §5.1, and §7:
/// discovery never touches secrets or loads assemblies, plugin rendering stays away from command
/// orchestration, and the store validates hashes without orchestrating tracking.
/// </summary>
public sealed class PluginBoundaryInvariantsTests
{
    private static readonly Type[] DiscoveryTypes =
    [
        typeof(PluginFileParser),
        typeof(PluginLoader),
        typeof(PluginCatalog),
        typeof(PluginManifest),
        typeof(PluginDescriptor),
        typeof(PluginCapability),
        typeof(PluginConfiguration),
        typeof(PluginSelector),
        typeof(PluginDirectoryResolver),
    ];

    [Fact]
    public void PluginDiscovery_DoesNotDependOnTheSecretStore()
    {
        IArchRule rule = Types().That().Are(DiscoveryTypes)
            .Should().NotDependOnAny(FlushedYoArchitecture.SecretStoreLayer)
            .Because("discovery reads JSON and checks file existence only; it must never call the secret store or start MCP servers (Architecture.md §3, §7).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }

    [Fact]
    public void PluginDiscovery_DoesNotDependOnAssemblyLoading()
    {
        IObjectProvider<IType> loadingTypes = Types().That()
            .HaveFullName("FlushedYo.Core.Plugin.PluginAssemblyLoader")
            .Or().HaveFullName("FlushedYo.Core.Plugin.PluginLoadContext")
            .Or().HaveFullName("FlushedYo.Core.Plugin.LoadedPlugin")
            .As("plugin assembly loading types");

        IArchRule rule = Types().That().Are(DiscoveryTypes)
            .Should().NotDependOnAny(loadingTypes)
            .Because("discovery never loads or instantiates plugin code; that is the assembly loader's job (Architecture.md §3, §7).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }

    [Fact]
    public void Plugin_DoesNotReachIntoCommandOrchestration()
    {
        IArchRule rule = Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Plugin")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Commands").And().DoNotHaveName("TextTable"))
            .Because("plugin rendering may use the TextTable primitive but must not depend on command orchestration (Architecture.md §4).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }

    [Fact]
    public void Store_DoesNotOrchestrateTheDedupTracker()
    {
        IArchRule rule = Types().That().ResideInNamespaceOrBelow("FlushedYo.Core.Store")
            .Should().NotDependOnAny(typeof(TransactionTracker), typeof(TrackingResult))
            .Because("the store re-validates the hash with CanonicalTransactionHasher at the storage boundary only; TransactionTracker stays the sole orchestrator (Architecture.md §5.1).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }
}
