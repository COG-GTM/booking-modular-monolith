using Flight.Data.Seed;
using Flight.Seats.Dtos;
using Flight.Seats.Models;
using FluentAssertions;
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
    public void seeded_seat_read_model_keeps_the_raw_seat_number()
    {
        var seat = InitialData.Seats.First();

        var readModel = _mapper.Map<SeatReadModel>(seat);

        readModel.SeatNumber.Should().Be(seat.SeatNumber.Value);
        readModel.SeatId.Should().Be(seat.Id.Value);
    }
}
