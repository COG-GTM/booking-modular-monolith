namespace Unit.Test.Seat.Domains;

using System;
using FluentAssertions;
using global::Flight.Flights.ValueObjects;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Features.CreatingSeat.V1;
using global::Flight.Seats.Features.ReservingSeat.V1;
using global::Flight.Seats.Models;
using global::Flight.Seats.ValueObjects;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class SeatTests
{
    [Fact]
    public void create_sets_properties_and_queues_created_event_with_ids()
    {
        var id = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        var seat = Seat.Create(
            SeatId.Of(id),
            SeatNumber.Of("12A"),
            SeatType.Window,
            SeatClass.Economy,
            FlightId.Of(flightId)
        );

        seat.Id.Value.Should().Be(id);
        seat.FlightId.Value.Should().Be(flightId);
        seat.SeatNumber.Value.Should().Be("12A");
        var createdEvent = seat
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<SeatCreatedDomainEvent>()
            .Subject;
        createdEvent.Id.Should().Be(id);
        createdEvent.FlightId.Should().Be(flightId);
    }

    [Fact]
    public void reserve_marks_deleted_sets_last_modified_and_queues_reserved_event()
    {
        var seat = Seat.Create(
            SeatId.Of(Guid.NewGuid()),
            SeatNumber.Of("12A"),
            SeatType.Window,
            SeatClass.Economy,
            FlightId.Of(Guid.NewGuid())
        );
        seat.ClearDomainEvents();

        seat.ReserveSeat();

        seat.IsDeleted.Should().BeTrue();
        seat.LastModified.Should().NotBeNull();
        var reservedEvent = seat
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<SeatReservedDomainEvent>()
            .Subject;
        reservedEvent.Id.Should().Be(seat.Id.Value);
        reservedEvent.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void seat_id_rejects_empty_guid_and_includes_it_in_the_message()
    {
        var act = () => SeatId.Of(Guid.Empty);

        act.Should()
            .Throw<global::Flight.Seats.Exceptions.InvalidSeatIdException>()
            .Which.Message.Should()
            .Contain(Guid.Empty.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void seat_number_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => SeatNumber.Of(value);

        act.Should().Throw<global::Flight.Seats.Exceptions.InvalidSeatNumberException>();
    }

    [Fact]
    public void seat_value_objects_convert_to_their_raw_values_and_record_values_compare_equal()
    {
        var id = Guid.NewGuid();
        var flightId = Guid.NewGuid();
        Guid rawId = SeatId.Of(id);
        Guid rawFlightId = FlightId.Of(flightId);
        string rawNumber = SeatNumber.Of("12A");

        rawId.Should().Be(id);
        rawFlightId.Should().Be(flightId);
        rawNumber.Should().Be("12A");
        SeatId.Of(id).Should().Be(SeatId.Of(id));
        SeatNumber.Of("12A").Should().Be(SeatNumber.Of("12A"));
    }
}
