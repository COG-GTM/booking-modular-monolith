using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contracts.Messages.Test;

public class ContractsPackageBoundaryTests
{
    public static IEnumerable<object[]> ExportedContractTypes =>
        typeof(UserCreated)
            .Assembly.GetExportedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new object[] { type });

    [Fact]
    public void BuildingBlocks_core_forwards_every_exported_contract_type()
    {
        var forwarded = typeof(BuildingBlocks.Core.IntegrationEventWrapper<>)
            .Assembly.GetForwardedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var exported = typeof(UserCreated)
            .Assembly.GetExportedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(exported, forwarded);
    }

    [Theory]
    [MemberData(nameof(ExportedContractTypes))]
    public void Legacy_core_assembly_qualified_name_resolves_to_contracts_type(Type type)
    {
        Assert.Same(type, Type.GetType($"{type.FullName}, BuildingBlocks.Core", throwOnError: true));
    }

    [Fact]
    public async Task Only_the_integration_event_marker_is_excluded_from_topology()
    {
        Assert.True(typeof(IIntegrationEvent).IsDefined(typeof(ExcludeFromTopologyAttribute), false));
        Assert.False(typeof(IEvent).IsDefined(typeof(ExcludeFromTopologyAttribute), false));

        await using var provider = new ServiceCollection().AddMassTransitTestHarness().BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        Assert.True(harness.Bus.Topology.PublishTopology.GetMessageTopology<IIntegrationEvent>().Exclude);
        Assert.False(harness.Bus.Topology.PublishTopology.GetMessageTopology<UserCreated>().Exclude);
    }

    [Fact]
    public void Event_type_is_the_contracts_assembly_qualified_name()
    {
        IEvent evt = new UserCreated(Guid.NewGuid(), "name", "P1");

        Assert.Equal(typeof(UserCreated).AssemblyQualifiedName, evt.EventType);
        Assert.Same(typeof(UserCreated), Type.GetType(evt.EventType, throwOnError: true));
        Assert.Equal("Contracts.Messages", typeof(UserCreated).Assembly.GetName().Name);
    }
}
