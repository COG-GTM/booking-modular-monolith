using BuildingBlocks.TestBase;
using Contracts.Grpc.Flight;
using Flight.Data;
using Flight.Flights.Enums;
using Flight.Flights.Models;
using MongoDB.Driver;
using Xunit;

namespace Flight.Host.Test.Grpc;

public sealed class FlightGrpcServiceTests(
    TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext> fixture
) : FlightHostTestBase(fixture)
{
    [Fact]
    public async Task get_by_id_returns_a_flight_from_the_read_model()
    {
        var flight = CreateFlightReadModel();
        await Fixture.ExecuteReadContextAsync(db => db.Flight.InsertOneAsync(flight));
        var client = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        var response = await client.GetByIdAsync(new GetByIdRequest { Id = flight.Id.ToString() });

        Assert.Equal(flight.Id.ToString(), response.FlightDto.Id);
        Assert.Equal(flight.FlightNumber, response.FlightDto.FlightNumber);
        Assert.Equal(
            new DateTimeOffset(flight.DepartureDate).ToUnixTimeMilliseconds(),
            new DateTimeOffset(response.FlightDto.DepartureDate.ToDateTime()).ToUnixTimeMilliseconds()
        );
        Assert.Equal(
            new DateTimeOffset(flight.ArriveDate).ToUnixTimeMilliseconds(),
            new DateTimeOffset(response.FlightDto.ArriveDate.ToDateTime()).ToUnixTimeMilliseconds()
        );
        Assert.Equal(
            new DateTimeOffset(flight.FlightDate).ToUnixTimeMilliseconds(),
            new DateTimeOffset(response.FlightDto.FlightDate.ToDateTime()).ToUnixTimeMilliseconds()
        );
    }

    internal static FlightReadModel CreateFlightReadModel()
    {
        var now = DateTime.UtcNow;
        var flightId = Guid.NewGuid();
        return new FlightReadModel
        {
            Id = flightId,
            FlightId = flightId,
            FlightNumber = "AB502",
            AircraftId = Guid.NewGuid(),
            DepartureDate = now.AddHours(1),
            DepartureAirportId = Guid.NewGuid(),
            ArriveDate = now.AddHours(2),
            ArriveAirportId = Guid.NewGuid(),
            DurationMinutes = 60,
            FlightDate = now,
            Status = global::Flight.Flights.Enums.FlightStatus.Flying,
            Price = 100,
            IsDeleted = false,
        };
    }
}
