using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using MassTransit;
using MediatR;
using Xunit;

namespace Contracts.Messages.Tests;

public class ContractsTopologyTests
{
    [Fact]
    public void integration_event_marker_is_excluded_from_broker_topology()
    {
        typeof(IIntegrationEvent).Should().BeDecoratedWith<ExcludeFromTopologyAttribute>();
        typeof(IIntegrationEvent).Should().Implement<IEvent>();
        typeof(IEvent).Should().Implement<INotification>();
    }

    [Fact]
    public void all_published_events_are_owned_by_the_contracts_assembly()
    {
        Type[] expected =
        [
            typeof(FlightCreated),
            typeof(FlightUpdated),
            typeof(FlightDeleted),
            typeof(AircraftCreated),
            typeof(AirportCreated),
            typeof(SeatCreated),
            typeof(SeatReserved),
            typeof(UserCreated),
            typeof(PassengerRegistrationCompleted),
            typeof(PassengerCreated),
            typeof(BookingCreated),
        ];

        ContractTypes.Messages.Should().BeEquivalentTo(expected);
        expected
            .Should()
            .AllSatisfy(type =>
            {
                type.Assembly.Should().BeSameAs(typeof(IEvent).Assembly);
                type.Namespace.Should().Be("BuildingBlocks.Contracts.EventBus.Messages");
                type.Should().Implement<IIntegrationEvent>();
                type.Should().NotBeDecoratedWith<ExcludeFromTopologyAttribute>();
            });
    }
}
