using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

using global::Flight.Data.Seed;
using global::Flight.Flights.Models;
using global::Flight.Flights.ValueObjects;

public class UpdateFlightTests : FlightEndToEndTestBase
{
    public UpdateFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_update_flight_when_user_is_admin()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var request = new FakeUpdateFlightCommand(flightEntity).Generate();

        // Act
        var route = ApiRoutes.Flight.UpdateFlight;
        var result = await Fixture.CreateHttpClient(IdentityConstant.Role.Admin).PutAsJsonAsync(route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_return_forbidden_when_user_is_not_admin()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var request = new FakeUpdateFlightCommand(flightEntity).Generate();

        // Act
        var route = ApiRoutes.Flight.UpdateFlight;
        var result = await Fixture.CreateHttpClient(IdentityConstant.Role.User).PutAsJsonAsync(route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var persisted = await Fixture.FindAsync<Flight, FlightId>(flightEntity.Id);
        persisted.Price.Value.Should().Be(flightEntity.Price.Value);
    }

    [Fact]
    public async Task should_return_bad_request_when_update_flight_request_is_invalid()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var request = new FakeUpdateFlightCommand(flightEntity).Generate() with { Price = -1 };

        // Act
        var route = ApiRoutes.Flight.UpdateFlight;
        var result = await Fixture.CreateHttpClient(IdentityConstant.Role.Admin).PutAsJsonAsync(route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var persisted = await Fixture.FindAsync<Flight, FlightId>(flightEntity.Id);
        persisted.Price.Value.Should().Be(flightEntity.Price.Value);
    }

    [Fact]
    public async Task should_return_bad_request_when_update_flight_request_has_empty_id()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var request = new FakeUpdateFlightCommand(flightEntity).Generate() with { Id = Guid.Empty };

        // Act
        var route = ApiRoutes.Flight.UpdateFlight;
        var result = await Fixture.CreateHttpClient(IdentityConstant.Role.Admin).PutAsJsonAsync(route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
