namespace Unit.Test.Flight;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using global::Flight.Aircrafts.Features.CreatingAircraft.V1;
using global::Flight.Airports.Features.CreatingAirport.V1;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.CreatingFlight.V1;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Features.CreatingSeat.V1;
using global::Flight.Seats.Features.ReservingSeat.V1;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightEventMapperTests
{
    private static readonly Guid Id = Guid.Parse("26ea3b77-1c6b-4a43-bc07-28d871480acd");
    private static readonly DateTime DepartureDate = new(2035, 1, 1, 8, 0, 0);
    private static readonly DateTime ArriveDate = new(2035, 1, 1, 10, 0, 0);
    private static readonly DateTime FlightDate = new(2035, 1, 1);

    public static IEnumerable<object[]> EventCases
    {
        get
        {
            yield return new object[]
            {
                new FlightCreatedDomainEvent(
                    Id,
                    "FL123",
                    Id,
                    DepartureDate,
                    Id,
                    ArriveDate,
                    Id,
                    120,
                    FlightDate,
                    FlightStatus.Flying,
                    200,
                    false
                ),
                typeof(FlightCreated),
                typeof(global::Flight.Flights.Features.CreatingFlight.V1.CreateFlightMongo),
            };
            yield return new object[]
            {
                new FlightUpdatedDomainEvent(
                    Id,
                    "FL123",
                    Id,
                    DepartureDate,
                    Id,
                    ArriveDate,
                    Id,
                    120,
                    FlightDate,
                    FlightStatus.Delay,
                    250,
                    false
                ),
                typeof(FlightUpdated),
                typeof(global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlightMongo),
            };
            yield return new object[]
            {
                new FlightDeletedDomainEvent(
                    Id,
                    "FL123",
                    Id,
                    DepartureDate,
                    Id,
                    ArriveDate,
                    Id,
                    120,
                    FlightDate,
                    FlightStatus.Canceled,
                    300,
                    true
                ),
                typeof(FlightDeleted),
                typeof(global::Flight.Flights.Features.DeletingFlight.V1.DeleteFlightMongo),
            };
            yield return new object[]
            {
                new AircraftCreatedDomainEvent(Id, "Airbus", "A320", 2020, false),
                typeof(AircraftCreated),
                typeof(global::Flight.Aircrafts.Features.CreatingAircraft.V1.CreateAircraftMongo),
            };
            yield return new object[]
            {
                new AirportCreatedDomainEvent(Id, "Lisbon", "Terminal 1", "LIS", false),
                typeof(AirportCreated),
                typeof(global::Flight.Airports.Features.CreatingAirport.V1.CreateAirportMongo),
            };
            yield return new object[]
            {
                new SeatCreatedDomainEvent(Id, "12A", SeatType.Window, SeatClass.Economy, Id, false),
                typeof(SeatCreated),
                typeof(global::Flight.Seats.Features.CreatingSeat.V1.CreateSeatMongo),
            };
            yield return new object[]
            {
                new SeatReservedDomainEvent(Id, "12A", SeatType.Window, SeatClass.Economy, Id, true),
                typeof(SeatReserved),
                typeof(global::Flight.Seats.Features.ReservingSeat.V1.ReserveSeatMongo),
            };
        }
    }

    [Theory]
    [MemberData(nameof(EventCases))]
    public void maps_each_domain_event_to_its_integration_event_and_copies_id(
        IDomainEvent domainEvent,
        Type integrationEventType,
        Type _
    )
    {
        var mapped = new global::Flight.FlightEventMapper().MapToIntegrationEvent(domainEvent);

        mapped.Should().NotBeNull();
        mapped.Should().BeOfType(integrationEventType);
        mapped!.GetType().GetProperty("Id")!.GetValue(mapped).Should().Be(Id);
    }

    [Theory]
    [MemberData(nameof(EventCases))]
    public void maps_each_domain_event_to_its_internal_command_and_copies_fields(
        IDomainEvent domainEvent,
        Type _,
        Type internalCommandType
    )
    {
        var mapped = new global::Flight.FlightEventMapper().MapToInternalCommand(domainEvent);

        mapped.Should().NotBeNull();
        mapped.Should().BeOfType(internalCommandType);
        foreach (var property in domainEvent.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var destination = internalCommandType.GetProperty(property.Name);
            destination.Should().NotBeNull();
            destination!.GetValue(mapped).Should().Be(property.GetValue(domainEvent));
        }
    }

    [Fact]
    public void unknown_domain_event_maps_to_null()
    {
        var mapper = new global::Flight.FlightEventMapper();

        mapper.MapToIntegrationEvent(new UnknownEvent()).Should().BeNull();
        mapper.MapToInternalCommand(new UnknownEvent()).Should().BeNull();
    }

    private sealed record UnknownEvent : IDomainEvent;
}
