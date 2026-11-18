using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Data.Seed;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

using global::Flight.Flights.Features.UpdatingFlight.V1;
using FlightEntity = global::Flight.Flights.Models.Flight;
using FlightId = global::Flight.Flights.ValueObjects.FlightId;

public class UpdateFlightTests : FlightEndToEndTestBase
{
    public UpdateFlightTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    ) : base(integrationTestFixture) { }

    [Fact]
    public async Task only_admins_can_update_a_flight()
    {
        var flight = await Fixture.FindAsync<FlightEntity, FlightId>(InitialData.Flights.First().Id);
        var request = new UpdateFlightRequestDto(
            flight.Id.Value,
            flight.FlightNumber.Value,
            flight.AircraftId.Value,
            flight.DepartureAirportId.Value,
            flight.DepartureDate.Value,
            flight.ArriveDate.Value,
            flight.ArriveAirportId.Value,
            flight.DurationMinutes.Value,
            flight.FlightDate.Value,
            flight.Status,
            flight.Price.Value
        );

        var userResponse = await Fixture
            .CreateHttpClient(IdentityConstant.Role.User)
            .PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, request);

        userResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminResponse = await Fixture.HttpClient.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, request);

        adminResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
