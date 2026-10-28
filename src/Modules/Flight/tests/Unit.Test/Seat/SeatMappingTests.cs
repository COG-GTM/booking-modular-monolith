using Flight.Flights.ValueObjects;
using Flight.Seats.Dtos;
using Flight.Seats.Models;
using Flight.Seats.ValueObjects;
using MapsterMapper;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Seat;

[Collection(nameof(UnitTestFixture))]
public class SeatMappingTests
{
    private readonly UnitTestFixture _fixture;
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
    public void should_map_seat_number_value_to_read_model()
    {
        var seat = global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(Guid.NewGuid()),
            SeatNumber.Of("12A"),
            global::Flight.Seats.Enums.SeatType.Window,
            global::Flight.Seats.Enums.SeatClass.Economy,
            FlightId.Of(Guid.NewGuid())
        );

        var readModel = _mapper.Map<SeatReadModel>(seat);

        Assert.Equal("12A", readModel.SeatNumber);
    }

    [Fact]
    public void should_map_seat_id_from_read_model()
    {
        var seatId = Guid.NewGuid();
        var readModel = new SeatReadModel
        {
            Id = Guid.NewGuid(),
            SeatId = seatId,
            SeatNumber = "12A",
            Type = global::Flight.Seats.Enums.SeatType.Window,
            Class = global::Flight.Seats.Enums.SeatClass.Economy,
            FlightId = Guid.NewGuid(),
            IsDeleted = false,
        };

        var dto = _mapper.Map<SeatDto>(readModel);

        Assert.Equal(seatId, dto.Id);
    }
}