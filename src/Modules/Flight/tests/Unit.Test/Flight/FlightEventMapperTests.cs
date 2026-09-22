namespace Unit.Test.Flight;

using System.Linq;
using global::Flight;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using global::Flight.Flights.Features.CreatingFlight.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightEventMapperTests
{
    private readonly IEventMapper _mapper = new CompositeEventMapper([new FlightEventMapper()]);

    private sealed record UnknownDomainEvent : IDomainEvent;

    [Fact]
    public void should_map_flight_created_domain_event_to_integration_event()
    {
        // Arrange
        var flight = FakeFlightCreate.Generate();
        var domainEvent = flight.DomainEvents.OfType<FlightCreatedDomainEvent>().Single();

        // Act
        var integrationEvent = _mapper.MapToIntegrationEvent(domainEvent);

        // Assert
        integrationEvent.Should().BeOfType<FlightCreated>();
        ((FlightCreated)integrationEvent!).Id.Should().Be(domainEvent.Id);
    }

    [Fact]
    public void should_map_flight_created_domain_event_to_internal_command()
    {
        // Arrange
        var flight = FakeFlightCreate.Generate();
        var domainEvent = flight.DomainEvents.OfType<FlightCreatedDomainEvent>().Single();

        // Act
        var internalCommand = _mapper.MapToInternalCommand(domainEvent);

        // Assert
        internalCommand.Should().BeOfType<CreateFlightMongo>();
        var command = (CreateFlightMongo)internalCommand!;
        command.Id.Should().Be(domainEvent.Id);
        command.FlightNumber.Should().Be(domainEvent.FlightNumber);
        command.AircraftId.Should().Be(domainEvent.AircraftId);
        command.Price.Should().Be(domainEvent.Price);
    }

    [Fact]
    public void should_return_null_for_domain_events_outside_flight_module()
    {
        // Arrange
        var domainEvent = new UnknownDomainEvent();

        // Act + Assert
        _mapper.MapToIntegrationEvent(domainEvent).Should().BeNull();
        _mapper.MapToInternalCommand(domainEvent).Should().BeNull();
    }
}
