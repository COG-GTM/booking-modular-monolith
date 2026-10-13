using Flight.Flights.ValueObjects;
using Flight.Seats.Dtos;
using Flight.Seats.Enums;
using Flight.Seats.Models;
using Flight.Seats.ValueObjects;
using FluentAssertions;
using MapsterMapper;
using MassTransit;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Seat;

// Regression tests for the seat read-model mappings fixed alongside the standalone Flight host:
// GetAvailableSeats must hand out ids that ReserveSeat accepts (SeatId, not the Mongo document id)
// and SeatNumber must be flattened to its raw value.
[Collection(nameof(UnitTestFixture))]
public class SeatReadModelMappingTests
{
    private readonly IMapper _mapper;

    public SeatReadModelMappingTests(UnitTestFixture fixture)
    {
        _mapper = fixture.Mapper;
    }

    [Fact]
    public void seat_to_read_model_should_store_the_raw_seat_number_and_seat_id()
    {
        var seat = CreateSeat();

        var readModel = _mapper.Map<SeatReadModel>(seat);

        readModel.SeatNumber.Should().Be(seat.SeatNumber.Value);
        readModel.SeatNumber.Should().NotContain("Value");
        readModel.SeatId.Should().Be(seat.Id.Value);
        readModel.FlightId.Should().Be(seat.FlightId.Value);
        readModel.Id.Should().NotBeEmpty();
        readModel.Id.Should().NotBe(seat.Id.Value);
    }

    [Fact]
    public void read_model_to_dto_should_expose_the_seat_id_not_the_document_id()
    {
        var readModel = new SeatReadModel
        {
            Id = NewId.NextGuid(),
            SeatId = NewId.NextGuid(),
            SeatNumber = "12A",
            Type = SeatType.Window,
            Class = SeatClass.Economy,
            FlightId = NewId.NextGuid(),
            IsDeleted = false,
        };

        var dto = _mapper.Map<SeatDto>(readModel);

        dto.Id.Should().Be(readModel.SeatId);
        dto.SeatNumber.Should().Be("12A");
        dto.Type.Should().Be(SeatType.Window);
        dto.Class.Should().Be(SeatClass.Economy);
        dto.FlightId.Should().Be(readModel.FlightId);
    }

    [Fact]
    public void seat_round_trip_through_the_read_model_should_keep_the_domain_id_usable_for_reserve_seat()
    {
        var seat = CreateSeat();

        var dto = _mapper.Map<SeatDto>(_mapper.Map<SeatReadModel>(seat));

        dto.Id.Should().Be(seat.Id.Value);
        dto.SeatNumber.Should().Be(seat.SeatNumber.Value);
        dto.FlightId.Should().Be(seat.FlightId.Value);
    }

    private static global::Flight.Seats.Models.Seat CreateSeat() =>
        global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(NewId.NextGuid()),
            SeatNumber.Of("14C"),
            SeatType.Aisle,
            SeatClass.Business,
            FlightId.Of(NewId.NextGuid())
        );
}
