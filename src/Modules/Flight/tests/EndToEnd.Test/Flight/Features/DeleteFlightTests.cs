using System;
using System.Linq;
using System.Net;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Data.Seed;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class DeleteFlightTests : FlightEndToEndTestBase
{
    public DeleteFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_return_forbidden_when_user_is_not_admin()
    {
        //Arrange
        var flightId = InitialData.Flights.First().Id.Value;
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Flight.DeleteFlight.Replace(ApiRoutes.Flight.Id, flightId.ToString(), StringComparison.CurrentCulture);
        var result = await httpClient.DeleteAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_delete_flight_when_user_is_admin()
    {
        //Arrange
        var flightId = InitialData.Flights.First().Id.Value;

        // Act
        var route = ApiRoutes.Flight.DeleteFlight.Replace(ApiRoutes.Flight.Id, flightId.ToString(), StringComparison.CurrentCulture);
        var result = await Fixture.HttpClient.DeleteAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
