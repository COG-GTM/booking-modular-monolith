using System.Reflection;
using System.Xml.Linq;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Xunit;

namespace Contracts.Messages.Tests;

// The contracts package must stay consumable by any service without dragging in module code or infrastructure.
public class PackageSurfaceTests
{
    private static readonly string ContractsProject =
        Path.Combine(ContractTypes.RepoRoot, "src", "Contracts", "Contracts.Messages", "Contracts.Messages.csproj");

    [Fact]
    public void project_has_no_project_references_and_only_abstraction_packages()
    {
        var project = XDocument.Load(ContractsProject);

        project.Descendants("ProjectReference").Should().BeEmpty();
        project.Descendants("FrameworkReference").Should().BeEmpty();
        project.Descendants("PackageReference").Select(x => x.Attribute("Include")!.Value).Should()
            .BeEquivalentTo("MassTransit.Abstractions", "MediatR.Contracts");
    }

    [Fact]
    public void assembly_references_only_the_runtime_and_abstraction_assemblies()
    {
        var allowed = new[] { "MassTransit.Abstractions", "MediatR.Contracts" };

        ContractTypes.Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard")
            .Should().BeSubsetOf(allowed);
    }

    [Fact]
    public void assembly_and_package_identity_are_versioned()
    {
        ContractTypes.Assembly.GetName().Name.Should().Be("Contracts.Messages");
        ContractTypes.Assembly.GetName().Version.Should().NotBeNull();
        ContractTypes.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion
            .Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void core_forwards_the_moved_event_interfaces()
    {
        var core = typeof(IDomainEvent).Assembly;

        core.GetName().Name.Should().Be("BuildingBlocks.Core");
        core.GetForwardedTypes().Should().Contain(new[] { typeof(IEvent), typeof(IIntegrationEvent) });
        typeof(IIntegrationEvent).Assembly.Should().BeSameAs(ContractTypes.Assembly);
    }

    [Theory]
    [InlineData("src/BuildingBlocks/Core/BuildingBlocks.Core.csproj")]
    [InlineData("src/BuildingBlocks/BuildingBlocks.csproj")]
    [InlineData("src/Modules/Identity/src/Identity.csproj")]
    [InlineData("src/Modules/Passenger/src/Passenger.csproj")]
    [InlineData("src/Modules/Flight/src/Flight.csproj")]
    [InlineData("src/Modules/Booking/src/Booking.csproj")]
    public void producers_and_consumers_reference_contracts_through_the_versioned_reference(string relativePath)
    {
        var project = XDocument.Load(Path.Combine(ContractTypes.RepoRoot, relativePath));

        project.Descendants("Import").Select(x => x.Attribute("Project")!.Value.Replace('\\', '/'))
            .Should().Contain(p => p.EndsWith("Contracts/Contracts.Messages.Reference.props", StringComparison.Ordinal));
        project.Descendants("ProjectReference").Select(x => x.Attribute("Include")!.Value)
            .Should().NotContain(p => p.Contains("Contracts", StringComparison.Ordinal));
    }

    [Fact]
    public void old_buildingblocks_contracts_project_is_gone()
    {
        Directory.Exists(Path.Combine(ContractTypes.RepoRoot, "src", "BuildingBlocks", "Contracts")).Should().BeFalse();
    }
}
