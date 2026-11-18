using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

using global::Flight.Data.Seed;
using global::Flight.Flights.Models;
using global::Flight.Flights.ValueObjects;

public class UpdateFlightAuthorizationTests : FlightEndToEndTestBase
{
    public UpdateFlightAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task should_challenge_anonymous_flight_update()
    {
        var flight = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var request = new FakeUpdateFlightCommand(flight).Generate();
        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;

        using var response = await client.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("identity-api")]
    public async Task should_forbid_admin_flight_update_without_required_scope(string? scope)
    {
        var flight = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var originalPrice = flight.Price.Value;
        var request = new FakeUpdateFlightCommand(flight).Generate() with { Price = originalPrice + 100 };
        using var client = Fixture.HttpClient;
        var claims = new Dictionary<string, object> { [ClaimTypes.Role] = IdentityConstant.Role.Admin };
        if (scope is not null)
        {
            claims["scope"] = scope;
        }

        client.SetFakeBearerToken(claims);

        using var response = await client.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var persisted = await Fixture.FindAsync<Flight, FlightId>(flight.Id);
        persisted.Price.Value.Should().Be(originalPrice);
    }
}
