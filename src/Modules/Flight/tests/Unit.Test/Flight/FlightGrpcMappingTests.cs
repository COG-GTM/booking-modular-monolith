using Flight.Flights.Dtos;
using Flight.Flights.Features;
using Mapster;
using MapsterMapper;
using Xunit;

namespace Unit.Test.Flight;

public class FlightGrpcMappingTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void should_normalize_all_grpc_timestamps_to_utc(DateTimeKind kind)
    {
        var config = new TypeAdapterConfig();
        new FlightMappings().Register(config);
        var mapper = new Mapper(config);
        var departure = new DateTime(2026, 10, 28, 10, 15, 30, kind).AddTicks(1234567);
        var flight = new FlightDto(
            Guid.NewGuid(),
            "AB502",
            Guid.NewGuid(),
            Guid.NewGuid(),
            departure,
            departure.AddHours(2),
            Guid.NewGuid(),
            120,
            departure.Date,
            global::Flight.Flights.Enums.FlightStatus.Flying,
            100
        );

        var response = mapper.Map<global::Contracts.Grpc.Flight.FlightResponse>(flight);

        Assert.Equal(flight.DepartureDate.ToUniversalTime(), response.DepartureDate.ToDateTime());
        Assert.Equal(flight.ArriveDate.ToUniversalTime(), response.ArriveDate.ToDateTime());
        Assert.Equal(flight.FlightDate.ToUniversalTime(), response.FlightDate.ToDateTime());
        Assert.Equal(DateTimeKind.Utc, response.DepartureDate.ToDateTime().Kind);
        Assert.Equal(DateTimeKind.Utc, response.ArriveDate.ToDateTime().Kind);
        Assert.Equal(DateTimeKind.Utc, response.FlightDate.ToDateTime().Kind);
        Assert.Equal(flight.Id.ToString(), response.Id);
        Assert.Equal(flight.FlightNumber, response.FlightNumber);
    }
}
