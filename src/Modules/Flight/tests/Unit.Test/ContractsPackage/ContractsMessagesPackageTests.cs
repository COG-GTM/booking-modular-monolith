using System.Reflection;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Utils;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.ContractsPackage;

public class ContractsMessagesPackageTests
{
    private const string ContractsAssemblyName = "Contracts.Messages";
    private const string ContractsNamespace = "BuildingBlocks.Contracts.EventBus.Messages";

    private static readonly Assembly ContractsAssembly = typeof(UserCreated).Assembly;

    private static readonly string[] InRepoAssemblies =
    {
        "BuildingBlocks",
        "BuildingBlocks.Core",
        "Api",
        "Booking",
        "Flight",
        "Identity",
        "Passenger",
    };

    public static IEnumerable<object[]> ContractTypes =>
        new[]
        {
            typeof(UserCreated),
            typeof(FlightCreated),
            typeof(FlightUpdated),
            typeof(FlightDeleted),
            typeof(AircraftCreated),
            typeof(AirportCreated),
            typeof(SeatCreated),
            typeof(SeatReserved),
            typeof(PassengerCreated),
            typeof(PassengerRegistrationCompleted),
            typeof(BookingCreated),
        }.Select(t => new object[] { t });

    [Fact]
    public void contracts_and_event_markers_live_in_the_standalone_contracts_assembly()
    {
        ContractsAssembly.GetName().Name.Should().Be(ContractsAssemblyName);
        typeof(IEvent).Assembly.Should().BeSameAs(ContractsAssembly);
        typeof(IIntegrationEvent).Assembly.Should().BeSameAs(ContractsAssembly);
    }

    [Fact]
    public void building_blocks_domain_event_still_derives_from_the_relocated_event_marker()
    {
        typeof(IDomainEvent).Assembly.GetName().Name.Should().Be("BuildingBlocks.Core");
        typeof(IDomainEvent).Should().Implement<IEvent>();
        typeof(IntegrationEventWrapper<IDomainEvent>).Should().Implement<IIntegrationEvent>();
    }

    [Fact]
    public void contracts_assembly_does_not_reference_building_blocks_or_any_module()
    {
        var referenced = ContractsAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.Should().NotContain(InRepoAssemblies);
        referenced.Should().Contain("MassTransit.Abstractions");
        referenced.Should().Contain("MediatR.Contracts");
        referenced.Should().NotContain(name => name!.StartsWith("MassTransit.") && name != "MassTransit.Abstractions");
        referenced.Should().NotContain(name => name!.StartsWith("Microsoft."));
    }

    [Fact]
    public void every_exported_message_is_an_integration_event_in_the_original_namespace()
    {
        var messages = ContractsAssembly.GetExportedTypes().Where(t => t.IsClass).ToList();

        messages.Should().NotBeEmpty();
        messages.Should().OnlyContain(t => t.Namespace == ContractsNamespace);
        messages.Should().OnlyContain(t => typeof(IIntegrationEvent).IsAssignableFrom(t));
        messages.Should().OnlyContain(t => t.GetMethod("<Clone>$") != null, "contracts are immutable records");
    }

    [Fact]
    public void package_still_exposes_every_contract_published_by_the_modules()
    {
        var exported = ContractsAssembly.GetExportedTypes().Select(t => t.FullName).ToList();

        var expected = ContractTypes.Select(x => ((Type)x[0]).FullName).ToList();

        exported.Should().Contain(expected);
        expected.Should().HaveCount(11);
    }

    [Fact]
    public void integration_event_marker_is_excluded_from_masstransit_topology()
    {
        typeof(IIntegrationEvent).GetCustomAttribute<ExcludeFromTopologyAttribute>().Should().NotBeNull();
        typeof(IEvent).GetCustomAttribute<ExcludeFromTopologyAttribute>().Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public void masstransit_message_urn_is_unchanged_by_the_move(Type contractType)
    {
        var urn = MessageUrn.ForTypeString(contractType);

        urn.Should().Be($"urn:message:{ContractsNamespace}:{contractType.Name}");
        urn.Should().NotContain(ContractsAssemblyName);
    }

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public void outbox_data_type_still_resolves_to_the_relocated_contract(Type contractType)
    {
        var storedDataType = contractType.ToString();

        storedDataType.Should().Be($"{ContractsNamespace}.{contractType.Name}");
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(storedDataType).Should().Be(contractType);
    }

    [Fact]
    public void event_defaults_are_provided_by_the_relocated_event_marker()
    {
        IEvent @event = new UserCreated(Guid.NewGuid(), "name", "passport");

        @event.EventId.Should().NotBeEmpty();
        @event.OccurredOn.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
        @event.EventType.Should().Be(typeof(UserCreated).AssemblyQualifiedName);
        @event.EventType.Should().StartWith($"{ContractsNamespace}.UserCreated, {ContractsAssemblyName},");
    }

    [Fact]
    public async Task relocated_contract_round_trips_through_the_masstransit_bus()
    {
        var services = new ServiceCollection();
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<UserCreatedTestConsumer>());

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var userCreated = new UserCreated(Guid.NewGuid(), "name", "passport");

        await harness.Bus.Publish(userCreated);

        (await harness.Consumed.Any<UserCreated>()).Should().BeTrue();

        var consumerHarness = harness.GetConsumerHarness<UserCreatedTestConsumer>();
        (await consumerHarness.Consumed.Any<UserCreated>(x => x.Context.Message == userCreated)).Should().BeTrue();

        var consumed = await harness.Consumed.SelectAsync<UserCreated>().FirstOrDefault();
        consumed.Should().NotBeNull();
        var supportedMessageTypes = consumed!.Context.SupportedMessageTypes.ToList();
        supportedMessageTypes.Should().Contain(MessageUrn.ForTypeString<UserCreated>());

        var topology = provider.GetRequiredService<IBus>().Topology;
        topology.Publish<UserCreated>().Exclude.Should().BeFalse();
        topology.Publish<IIntegrationEvent>().Exclude.Should().BeTrue();
    }

    private class UserCreatedTestConsumer : IConsumer<UserCreated>
    {
        public Task Consume(ConsumeContext<UserCreated> context) => Task.CompletedTask;
    }
}
