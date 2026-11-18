using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Data.Seed;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.Models;
using global::Flight.Flights.ValueObjects;

public class DeleteFlightTests : FlightIntegrationTestBase
{
    public DeleteFlightTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_delete_flight_from_db()
    {
        // Arrange
        var flightEntity = await Fixture.FindAsync<Flight, FlightId>(InitialData.Flights.First().Id);
        var command = new DeleteFlight(flightEntity.Id.Value);

        // Act
        await Fixture.SendAsync(command);
        var deletedFlight = (await Fixture.ExecuteDbContextAsync(db => db.Flights
                .Where(x => x.Id == FlightId.Of(command.Id))
                .IgnoreQueryFilters()
                .ToListAsync())
            ).FirstOrDefault();

        // Assert
        deletedFlight?.IsDeleted.Should().BeTrue();

        (await Fixture.WaitForPublishing<FlightDeleted>()).Should().Be(true);
    }

    [Fact]
    public async Task should_forbid_user_from_deleting_flight()
    {
        // Arrange
        var flightId = InitialData.Flights.First().Id.Value;
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        using var response = await client.DeleteAsync($"api/v1/flight/{flightId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var flight = await Fixture.ExecuteDbContextAsync(db =>
            db.Flights
                .Where(x => x.Id == FlightId.Of(flightId))
                .IgnoreQueryFilters()
                .SingleAsync()
        );
        flight.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_allow_admin_to_delete_flight()
    {
        // Arrange
        var flightId = InitialData.Flights.First().Id.Value;
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.Admin);

        // Act
        using var response = await client.DeleteAsync($"api/v1/flight/{flightId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_allow_user_to_get_available_flights()
    {
        // Arrange
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        using var response = await client.GetAsync("api/v1/flight/get-available-flights");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}