using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlushedYo.ArchitectureTests;

/// <summary>Every Core type must live in one of the documented component namespaces (docs/Architecture.md §4).</summary>
public sealed class CoreNamespaceOrganizationTests
{
    [Fact]
    public void AllCoreTypes_ResideInADocumentedComponentNamespace()
    {
        IArchRule rule = Types().That().ResideInAssembly(FlushedYoArchitecture.CoreAssembly)
            .Should().ResideInNamespaceMatching("^FlushedYo\\.Core\\.[A-Z][A-Za-z]+$")
            .Because("Core components map one-to-one onto namespaces (Models, Configuration, SecretStore, Plugin, Commands, Tracking, Store) in docs/Architecture.md §4.");
        rule.Check(FlushedYoArchitecture.Architecture);
    }
}
