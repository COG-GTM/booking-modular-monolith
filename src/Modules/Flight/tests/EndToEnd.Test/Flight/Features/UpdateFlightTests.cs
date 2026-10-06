using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Data.Seed;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class UpdateFlightTests : FlightEndToEndTestBase
{
    public UpdateFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_update_flight_when_user_is_admin()
    {
        //Arrange
        var command = new FakeUpdateFlightCommand(InitialData.Flights.First()).Generate();

        // Act
        var route = ApiRoutes.Flight.UpdateFlight;
        var result = await Fixture.HttpClient.PutAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_return_forbidden_when_user_is_not_admin()
    {
        //Arrange
        var command = new FakeUpdateFlightCommand(InitialData.Flights.First()).Generate();
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Flight.UpdateFlight;
        var result = await httpClient.PutAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
