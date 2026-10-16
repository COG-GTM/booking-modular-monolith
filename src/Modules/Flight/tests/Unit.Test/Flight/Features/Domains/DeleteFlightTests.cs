namespace Unit.Test.Flight.Features.Domains;

using System;
using FluentAssertions;
using global::Flight.Aircrafts.ValueObjects;
using global::Flight.Airports.ValueObjects;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.Models;
using global::Flight.Flights.ValueObjects;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class DeleteFlightTests
{
    [Fact]
    public void delete_defaults_to_soft_deleted_and_queues_event_with_same_id()
    {
        var id = Guid.NewGuid();
        var flight = Flight.Create(
            FlightId.Of(id),
            FlightNumber.Of("FL123"),
            AircraftId.Of(Guid.NewGuid()),
            AirportId.Of(Guid.NewGuid()),
            DepartureDate.Of(new DateTime(2035, 1, 1, 8, 0, 0)),
            ArriveDate.Of(new DateTime(2035, 1, 1, 10, 0, 0)),
            AirportId.Of(Guid.NewGuid()),
            DurationMinutes.Of(120),
            FlightDate.Of(new DateTime(2035, 1, 1)),
            FlightStatus.Flying,
            Price.Of(100)
        );
        flight.ClearDomainEvents();

        flight.Delete(
            flight.Id,
            flight.FlightNumber,
            flight.AircraftId,
            flight.DepartureAirportId,
            flight.DepartureDate,
            flight.ArriveDate,
            flight.ArriveAirportId,
            flight.DurationMinutes,
            flight.FlightDate,
            flight.Status,
            flight.Price
        );

        flight.IsDeleted.Should().BeTrue();
        var deletedEvent = flight
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<FlightDeletedDomainEvent>()
            .Subject;
        deletedEvent.Id.Should().Be(id);
        deletedEvent.IsDeleted.Should().BeTrue();
    }
}
