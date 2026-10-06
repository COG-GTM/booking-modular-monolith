namespace Unit.Test.Seat;

using System;
using FluentAssertions;
using global::Flight.Flights.ValueObjects;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Models;
using global::Flight.Seats.ValueObjects;
using MapsterMapper;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class SeatReadModelMappingTests
{
    private readonly IMapper _mapper;

    public SeatReadModelMappingTests(UnitTestFixture fixture)
    {
        _mapper = fixture.Mapper;
    }

    [Fact]
    public void should_map_seat_to_seat_read_model_using_the_raw_seat_number_value()
    {
        var seatId = SeatId.Of(Guid.NewGuid());
        var flightId = FlightId.Of(Guid.NewGuid());
        var seat = global::Flight.Seats.Models.Seat.Create(
            seatId,
            SeatNumber.Of("12A"),
            SeatType.Window,
            SeatClass.Economy,
            flightId
        );

        var readModel = _mapper.Map<SeatReadModel>(seat);

        readModel.SeatNumber.Should().Be("12A");
        readModel.SeatId.Should().Be(seatId.Value);
        readModel.FlightId.Should().Be(flightId.Value);
        readModel.Type.Should().Be(SeatType.Window);
        readModel.Class.Should().Be(SeatClass.Economy);
        readModel.IsDeleted.Should().BeFalse();
        readModel.Id.Should().NotBe(Guid.Empty).And.NotBe(seatId.Value);
    }

    [Fact]
    public void should_map_reserved_seat_to_seat_read_model_as_deleted()
    {
        var seat = global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(Guid.NewGuid()),
            SeatNumber.Of("1B"),
            SeatType.Aisle,
            SeatClass.Business,
            FlightId.Of(Guid.NewGuid())
        );
        seat.ReserveSeat();

        var readModel = _mapper.Map<SeatReadModel>(seat);

        readModel.SeatNumber.Should().Be("1B");
        readModel.IsDeleted.Should().BeTrue();
    }
}
