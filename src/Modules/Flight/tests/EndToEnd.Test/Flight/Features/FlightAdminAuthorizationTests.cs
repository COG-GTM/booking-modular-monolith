using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using EndToEnd.Test.Routes;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class FlightAdminAuthorizationTests : FlightEndToEndTestBase
{
    public FlightAdminAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Theory]
    [InlineData("POST", ApiRoutes.Flight.CreateAircraft)]
    [InlineData("POST", ApiRoutes.Flight.CreateAirport)]
    [InlineData("POST", ApiRoutes.Flight.CreateFlight)]
    [InlineData("PUT", ApiRoutes.Flight.UpdateFlight)]
    [InlineData("DELETE", ApiRoutes.Flight.DeleteFlight)]
    [InlineData("POST", ApiRoutes.Flight.CreateSeat)]
    public async Task user_role_is_forbidden_from_flight_admin_routes(string method, string route)
    {
        if (route == ApiRoutes.Flight.DeleteFlight)
        {
            route = route.Replace(
                ApiRoutes.Flight.Id,
                Guid.NewGuid().ToString(),
                StringComparison.Ordinal
            );
        }

        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task admin_role_can_create_aircraft()
    {
        using var client = Fixture.HttpClient;
        var request = new
        {
            Name = $"Aircraft {Guid.NewGuid():N}",
            Model = $"MODEL-{Guid.NewGuid():N}",
            ManufacturingYear = 2020,
        };

        using var response = await client.PostAsJsonAsync(ApiRoutes.Flight.CreateAircraft, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task admin_role_can_create_airport()
    {
        using var client = Fixture.HttpClient;
        var request = new
        {
            Name = $"Airport {Guid.NewGuid():N}",
            Address = $"Address {Guid.NewGuid():N}",
            Code = Guid.NewGuid().ToString("N"),
        };

        using var response = await client.PostAsJsonAsync(ApiRoutes.Flight.CreateAirport, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
