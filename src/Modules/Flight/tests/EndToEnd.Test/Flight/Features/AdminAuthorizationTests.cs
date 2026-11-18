using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class AdminAuthorizationTests : FlightEndToEndTestBase
{
    public AdminAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task delete_flight_should_return_forbidden_when_caller_is_not_admin()
    {
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User, Guid.NewGuid());
        var route = ApiRoutes.Flight.DeleteFlight.Replace(ApiRoutes.Flight.Id, Guid.NewGuid().ToString(), StringComparison.Ordinal);

        var result = await httpClient.DeleteAsync(route);

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task create_aircraft_should_return_forbidden_when_caller_is_not_admin()
    {
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User, Guid.NewGuid());

        var result = await httpClient.PostAsJsonAsync(ApiRoutes.Aircraft.CreateAircraft,
            new { Name = "Boeing 737", Model = "B737", ManufacturingYear = 2005 });

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
