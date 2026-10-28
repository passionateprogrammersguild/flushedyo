using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlushedYo.ArchitectureTests;

/// <summary>Container layering between the CLI composition root and Core (docs/Architecture.md §3).</summary>
public sealed class CliLayeringTests
{
    [Fact]
    public void Core_DoesNotDependOnTheCliCompositionRoot()
    {
        IArchRule rule = Types().That().ResideInAssembly(FlushedYoArchitecture.CoreAssembly)
            .Should().NotDependOnAny(FlushedYoArchitecture.CliLayer)
            .Because("the CLI is a thin composition root and Core must stay reusable without it (Architecture.md §3, §9).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }

    [Fact]
    public void Cli_ContainsOnlyTheProgramCompositionRoot()
    {
        IArchRule rule = Types().That().ResideInAssembly(FlushedYoArchitecture.CliAssembly)
            .And().DoNotHaveName("Program")
            .And().AreNotNested()
            .Should().NotExist()
            .Because("the CLI is a thin composition root: its assembly must not grow domain types (Architecture.md §3).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }

    [Fact]
    public void Cli_DependsOnlyOnCoreAndTheFramework()
    {
        IObjectProvider<IType> allowed = Types().That()
            .ResideInNamespaceOrBelow("System")
            .Or().ResideInNamespaceOrBelow("Microsoft")
            .Or().ResideInNamespaceOrBelow("FlushedYo.Core")
            .Or().ResideInAssembly(FlushedYoArchitecture.CliAssembly)
            .As("the .NET framework, FlushedYo.Core, and the composition root itself");

        IArchRule rule = Types().That().ResideInAssembly(FlushedYoArchitecture.CliAssembly)
            .Should().OnlyDependOn(allowed)
            .Because("Program wires Core services and must not acquire domain logic or depend on anything outside Core and the framework (Architecture.md §3).");
        rule.Check(FlushedYoArchitecture.Architecture);
    }
}
