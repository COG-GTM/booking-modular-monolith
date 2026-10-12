using Flight.Flights.ValueObjects;
using Flight.Seats.Dtos;
using Flight.Seats.Enums;
using Flight.Seats.Models;
using Flight.Seats.ValueObjects;
using FluentAssertions;
using MapsterMapper;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Seat;

[Collection(nameof(UnitTestFixture))]
public class SeatMappingTests
{
    private readonly IMapper _mapper;

    public SeatMappingTests(UnitTestFixture fixture)
    {
        _mapper = fixture.Mapper;
    }

    public static IEnumerable<object[]> Data
    {
        get
        {
            yield return new object[]
            {
                // these types will instantiate with reflection in the future
                typeof(global::Flight.Seats.Models.SeatReadModel), typeof(SeatDto)
            };
        }
    }


    [Theory]
    [MemberData(nameof(Data))]
    public void should_support_mapping_from_source_to_destination(Type source, Type destination, params object[] parameters)
    {
        var instance = Activator.CreateInstance(source, parameters);

        _mapper.Map(instance, source, destination);
    }

    [Fact]
    public void should_map_seat_number_and_flight_id_to_read_model()
    {
        var flightId = Guid.NewGuid();
        var seat = global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(Guid.NewGuid()),
            SeatNumber.Of("12A"),
            SeatType.Window,
            SeatClass.Economy,
            FlightId.Of(flightId)
        );

        var readModel = _mapper.Map<SeatReadModel>(seat);

        readModel.SeatNumber.Should().Be("12A");
        readModel.FlightId.Should().Be(flightId);
    }
}
