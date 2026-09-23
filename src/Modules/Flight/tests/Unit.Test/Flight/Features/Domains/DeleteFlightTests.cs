namespace Unit.Test.Flight.Features.Domains;

using System;
using System.Linq;
using FluentAssertions;
using global::Flight.Aircrafts.ValueObjects;
using global::Flight.Airports.ValueObjects;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.ValueObjects;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class DeleteFlightTests
{
    [Fact]
    public void can_delete_valid_flight()
    {
        // Arrange
        var fakeFlight = FakeFlightCreate.Generate();
        fakeFlight.IsDeleted.Should().BeFalse();

        // Act
        FakeFlightDelete.Generate(fakeFlight);

        // Assert
        fakeFlight.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void delete_preserves_existing_fields_when_passed_current_state()
    {
        // Arrange
        var fakeFlight = FakeFlightCreate.Generate();
        var originalId = fakeFlight.Id;
        var originalFlightNumber = fakeFlight.FlightNumber;
        var originalAircraftId = fakeFlight.AircraftId;
        var originalDepartureAirportId = fakeFlight.DepartureAirportId;
        var originalArriveAirportId = fakeFlight.ArriveAirportId;
        var originalDepartureDate = fakeFlight.DepartureDate;
        var originalArriveDate = fakeFlight.ArriveDate;
        var originalDurationMinutes = fakeFlight.DurationMinutes;
        var originalFlightDate = fakeFlight.FlightDate;
        var originalStatus = fakeFlight.Status;
        var originalPrice = fakeFlight.Price;

        // Act
        FakeFlightDelete.Generate(fakeFlight);

        // Assert
        fakeFlight.Id.Should().Be(originalId);
        fakeFlight.FlightNumber.Should().Be(originalFlightNumber);
        fakeFlight.AircraftId.Should().Be(originalAircraftId);
        fakeFlight.DepartureAirportId.Should().Be(originalDepartureAirportId);
        fakeFlight.ArriveAirportId.Should().Be(originalArriveAirportId);
        fakeFlight.DepartureDate.Should().Be(originalDepartureDate);
        fakeFlight.ArriveDate.Should().Be(originalArriveDate);
        fakeFlight.DurationMinutes.Should().BeSameAs(originalDurationMinutes);
        fakeFlight.FlightDate.Should().Be(originalFlightDate);
        fakeFlight.Status.Should().Be(originalStatus);
        fakeFlight.Price.Should().BeSameAs(originalPrice);
        fakeFlight.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void delete_sets_every_field_on_aggregate()
    {
        // Arrange
        var fakeFlight = FakeFlightCreate.Generate();
        var newFlightNumber = FlightNumber.Of(fakeFlight.FlightNumber.Value + "-deleted");
        var newAircraftId = AircraftId.Of(Guid.NewGuid());
        var newDepartureAirportId = AirportId.Of(Guid.NewGuid());
        var newArriveAirportId = AirportId.Of(Guid.NewGuid());
        var newDepartureDate = DepartureDate.Of(new DateTime(2032, 3, 15, 7, 0, 0, DateTimeKind.Utc));
        var newArriveDate = ArriveDate.Of(new DateTime(2032, 3, 15, 10, 20, 0, DateTimeKind.Utc));
        var newDurationMinutes = DurationMinutes.Of(200);
        var newFlightDate = FlightDate.Of(new DateTime(2032, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        var newStatus = fakeFlight.Status == FlightStatus.Canceled ? FlightStatus.Completed : FlightStatus.Canceled;
        var newPrice = Price.Of(fakeFlight.Price.Value + 99);

        // Act
        fakeFlight.Delete(
            fakeFlight.Id,
            newFlightNumber,
            newAircraftId,
            newDepartureAirportId,
            newDepartureDate,
            newArriveDate,
            newArriveAirportId,
            newDurationMinutes,
            newFlightDate,
            newStatus,
            newPrice
        );

        // Assert
        fakeFlight.FlightNumber.Should().Be(newFlightNumber);
        fakeFlight.AircraftId.Should().Be(newAircraftId);
        fakeFlight.DepartureAirportId.Should().Be(newDepartureAirportId);
        fakeFlight.ArriveAirportId.Should().Be(newArriveAirportId);
        fakeFlight.DepartureAirportId.Should().NotBe(fakeFlight.ArriveAirportId);
        fakeFlight.DepartureDate.Should().Be(newDepartureDate);
        fakeFlight.ArriveDate.Should().Be(newArriveDate);
        fakeFlight.DepartureDate.Value.Should().NotBe(fakeFlight.ArriveDate.Value);
        fakeFlight.DurationMinutes.Value.Should().Be(200);
        fakeFlight.FlightDate.Should().Be(newFlightDate);
        fakeFlight.Status.Should().Be(newStatus);
        fakeFlight.Price.Value.Should().Be(newPrice.Value);
        fakeFlight.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void delete_with_explicit_is_deleted_false_does_not_mark_deleted()
    {
        // Arrange
        var fakeFlight = FakeFlightCreate.Generate();

        // Act
        fakeFlight.Delete(
            fakeFlight.Id,
            fakeFlight.FlightNumber,
            fakeFlight.AircraftId,
            fakeFlight.DepartureAirportId,
            fakeFlight.DepartureDate,
            fakeFlight.ArriveDate,
            fakeFlight.ArriveAirportId,
            fakeFlight.DurationMinutes,
            fakeFlight.FlightDate,
            fakeFlight.Status,
            fakeFlight.Price,
            isDeleted: false
        );

        // Assert
        fakeFlight.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void queue_domain_event_on_delete()
    {
        // Arrange
        var fakeFlight = FakeFlightCreate.Generate();
        fakeFlight.ClearDomainEvents();

        // Act
        FakeFlightDelete.Generate(fakeFlight);

        // Assert
        fakeFlight.DomainEvents.Count.Should().Be(1);
        fakeFlight.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(FlightDeletedDomainEvent));
    }

    [Fact]
    public void domain_event_on_delete_carries_deleted_state()
    {
        // Arrange
        var fakeFlight = FakeFlightCreate.Generate();
        fakeFlight.ClearDomainEvents();
        var newFlightNumber = FlightNumber.Of("DEL-999");
        var newAircraftId = AircraftId.Of(Guid.NewGuid());
        var newDepartureAirportId = AirportId.Of(Guid.NewGuid());
        var newArriveAirportId = AirportId.Of(Guid.NewGuid());
        var newDepartureDate = DepartureDate.Of(new DateTime(2033, 7, 20, 5, 30, 0, DateTimeKind.Utc));
        var newArriveDate = ArriveDate.Of(new DateTime(2033, 7, 20, 8, 0, 0, DateTimeKind.Utc));
        var newDurationMinutes = DurationMinutes.Of(150);
        var newFlightDate = FlightDate.Of(new DateTime(2033, 7, 20, 0, 0, 0, DateTimeKind.Utc));
        var newPrice = Price.Of(555.55m);

        // Act
        fakeFlight.Delete(
            fakeFlight.Id,
            newFlightNumber,
            newAircraftId,
            newDepartureAirportId,
            newDepartureDate,
            newArriveDate,
            newArriveAirportId,
            newDurationMinutes,
            newFlightDate,
            FlightStatus.Canceled,
            newPrice
        );

        // Assert
        var @event = fakeFlight
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<FlightDeletedDomainEvent>()
            .Subject;

        @event.Id.Should().Be(fakeFlight.Id.Value);
        @event.FlightNumber.Should().Be("DEL-999");
        @event.AircraftId.Should().Be(newAircraftId.Value);
        @event.DepartureAirportId.Should().Be(newDepartureAirportId.Value);
        @event.ArriveAirportId.Should().Be(newArriveAirportId.Value);
        @event.DepartureDate.Should().Be(newDepartureDate.Value);
        @event.ArriveDate.Should().Be(newArriveDate.Value);
        @event.DurationMinutes.Should().Be(150);
        @event.FlightDate.Should().Be(newFlightDate.Value);
        @event.Status.Should().Be(FlightStatus.Canceled);
        @event.Price.Should().Be(555.55m);
        @event.IsDeleted.Should().BeTrue();
    }
}
