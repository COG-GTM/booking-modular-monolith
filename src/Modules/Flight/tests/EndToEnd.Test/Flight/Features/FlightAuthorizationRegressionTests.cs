using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Flights.Features.UpdatingFlight.V1;
using Flight.Seats.Enums;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class FlightAuthorizationRegressionTests : FlightEndToEndTestBase
{
    public FlightAuthorizationRegressionTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData("POST", ApiRoutes.Flight.CreateAircraft)]
    [InlineData("POST", ApiRoutes.Flight.CreateAirport)]
    [InlineData("POST", ApiRoutes.Flight.CreateFlight)]
    [InlineData("PUT", ApiRoutes.Flight.UpdateFlight)]
    [InlineData("DELETE", ApiRoutes.Flight.DeleteFlight)]
    [InlineData("POST", ApiRoutes.Flight.CreateSeat)]
    public async Task anonymous_requests_are_challenged_on_admin_routes(string method, string route)
    {
        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;
        route = route.Replace(ApiRoutes.Flight.Id, Guid.NewGuid().ToString(), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task admin_can_update_flight()
    {
        var flight = InitialData.Flights.First();
        var request = new UpdateFlightRequestDto(
            flight.Id,
            "AUTH123",
            flight.AircraftId,
            flight.DepartureAirportId,
            flight.DepartureDate.Value,
            flight.ArriveDate.Value,
            flight.ArriveAirportId,
            flight.DurationMinutes.Value,
            flight.FlightDate.Value,
            flight.Status,
            800,
            false
        );
        using var client = Fixture.HttpClient;

        using var response = await client.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task admin_can_delete_flight()
    {
        using var client = Fixture.HttpClient;
        var route = ApiRoutes.Flight.DeleteFlight.Replace(
            ApiRoutes.Flight.Id,
            InitialData.Flights.First().Id.Value.ToString(),
            StringComparison.Ordinal
        );

        using var response = await client.DeleteAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task admin_can_create_seat()
    {
        using var client = Fixture.HttpClient;
        var request = new
        {
            SeatNumber = "99A",
            Type = SeatType.Window,
            Class = SeatClass.Economy,
            FlightId = InitialData.Flights.First().Id.Value,
        };

        using var response = await client.PostAsJsonAsync(ApiRoutes.Flight.CreateSeat, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("api/v1.0/flight/{id}")]
    [InlineData("api/v1.0/flight/get-available-flights")]
    [InlineData("api/v1.0/flight/get-available-seats/{id}")]
    public async Task user_can_read_flight_data(string route)
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);
        route = route.Replace("{id}", InitialData.Flights.First().Id.Value.ToString(), StringComparison.Ordinal);

        using var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task user_can_reserve_seat()
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);
        var request = new { FlightId = InitialData.Flights.First().Id.Value, SeatNumber = "12A" };

        using var response = await client.PostAsJsonAsync("api/v1.0/flight/reserve-seat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
