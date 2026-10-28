using System.Net;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Host.Test.Grpc;
using MongoDB.Driver;
using Xunit;

namespace Flight.Host.Test.Rest;

public sealed class FlightRestEndpointTests(
    TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext> fixture
) : FlightHostTestBase(fixture)
{
    [Fact]
    public async Task get_available_flights_returns_ok_after_read_model_insert()
    {
        var flight = FlightGrpcServiceTests.CreateFlightReadModel();
        await Fixture.ExecuteReadContextAsync(db => db.Flight.InsertOneAsync(flight));

        var response = await Fixture.HttpClient.GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
