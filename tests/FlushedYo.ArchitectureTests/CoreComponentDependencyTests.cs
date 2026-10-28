using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlushedYo.ArchitectureTests;

/// <summary>
/// Enforces the Core component dependency matrix from docs/Architecture.md §4:
/// commands and services sit on top of plugin/secret-store/tracking/store; lower layers never
/// reach upward. The two documented implementation edges (Plugin → Commands for TextTable
/// rendering, Store → Tracking for hash validation) are allowed and pinned separately in
/// <see cref="PluginBoundaryInvariantsTests"/>.
/// </summary>
public sealed class CoreComponentDependencyTests
{
    [Fact]
    public void Models_DoNotDependOnAnyOtherCoreComponent()
    {
        Check(FlushedYoArchitecture.ModelsLayer,
            [FlushedYoArchitecture.ConfigurationLayer, FlushedYoArchitecture.SecretStoreLayer,
             FlushedYoArchitecture.PluginLayer, FlushedYoArchitecture.CommandsLayer,
             FlushedYoArchitecture.TrackingLayer, FlushedYoArchitecture.StoreLayer],
            "Models is the dependency-free canonical-record layer (Architecture.md §4).");
    }

    [Fact]
    public void Configuration_DoesNotDependOnAnyOtherCoreComponent()
    {
        Check(FlushedYoArchitecture.ConfigurationLayer,
            [FlushedYoArchitecture.ModelsLayer, FlushedYoArchitecture.SecretStoreLayer,
             FlushedYoArchitecture.PluginLayer, FlushedYoArchitecture.CommandsLayer,
             FlushedYoArchitecture.TrackingLayer, FlushedYoArchitecture.StoreLayer],
            "Configuration is a leaf support layer; options and path resolution depend only on the framework (Architecture.md §4).");
    }

    [Fact]
    public void SecretStore_DependsOnlyOnConfiguration()
    {
        Check(FlushedYoArchitecture.SecretStoreLayer,
            [FlushedYoArchitecture.ModelsLayer, FlushedYoArchitecture.PluginLayer,
             FlushedYoArchitecture.CommandsLayer, FlushedYoArchitecture.TrackingLayer,
             FlushedYoArchitecture.StoreLayer],
            "SecretStore implementations bind their own options but never depend on commands, plugins, tracking, or the ledger (Architecture.md §4).");
    }

    [Fact]
    public void Tracking_DependsOnlyOnModelsAndStore()
    {
        Check(FlushedYoArchitecture.TrackingLayer,
            [FlushedYoArchitecture.ConfigurationLayer, FlushedYoArchitecture.SecretStoreLayer,
             FlushedYoArchitecture.PluginLayer, FlushedYoArchitecture.CommandsLayer],
            "Tracking feeds the store idempotently and must not depend on command, plugin, secret, or configuration layers (Architecture.md §4).");
    }

    [Fact]
    public void Store_DependsOnlyOnConfigurationModelsAndTracking()
    {
        Check(FlushedYoArchitecture.StoreLayer,
            [FlushedYoArchitecture.SecretStoreLayer, FlushedYoArchitecture.PluginLayer,
             FlushedYoArchitecture.CommandsLayer],
            "Store may bind the ledger path and use the canonical hasher, but never depends on secrets, plugins, or commands (Architecture.md §4).");
    }

    [Fact]
    public void Plugin_DoesNotDependOnTrackingOrStore()
    {
        Check(FlushedYoArchitecture.PluginLayer,
            [FlushedYoArchitecture.TrackingLayer, FlushedYoArchitecture.StoreLayer],
            "Plugin machinery must not reach into the ledger or the dedup pipeline (Architecture.md §7).");
    }

    [Fact]
    public void Commands_DoNotDependOnConfiguration()
    {
        Check(FlushedYoArchitecture.CommandsLayer,
            [FlushedYoArchitecture.ConfigurationLayer],
            "Commands orchestrate services and never bind options directly; configuration stays in Program and its leaf consumers (Architecture.md §4).");
    }

    private static void Check(
        IObjectProvider<IType> component,
        IReadOnlyList<IObjectProvider<IType>> forbidden,
        string because)
    {
        IArchRule rule = Types().That().Are(component)
            .Should().NotDependOnAny(FlushedYoArchitecture.Union([.. forbidden]))
            .Because(because);
        rule.Check(FlushedYoArchitecture.Architecture);
    }
}
