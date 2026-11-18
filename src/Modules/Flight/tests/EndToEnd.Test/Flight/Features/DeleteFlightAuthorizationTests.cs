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

public class DeleteFlightAuthorizationTests : FlightEndToEndTestBase
{
    public DeleteFlightAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_forbid_user_from_deleting_a_flight()
    {
        var flightId = (Guid)InitialData.Flights.First().Id;
        var route = ApiRoutes.Flight.DeleteFlight.Replace(
            ApiRoutes.Flight.Id,
            flightId.ToString(),
            StringComparison.Ordinal
        );
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        var result = await client.DeleteAsync(route);

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_allow_admin_to_delete_a_flight()
    {
        var flightId = (Guid)InitialData.Flights.First().Id;
        var route = ApiRoutes.Flight.DeleteFlight.Replace(
            ApiRoutes.Flight.Id,
            flightId.ToString(),
            StringComparison.Ordinal
        );
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.Admin);

        var result = await client.DeleteAsync(route);

        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
