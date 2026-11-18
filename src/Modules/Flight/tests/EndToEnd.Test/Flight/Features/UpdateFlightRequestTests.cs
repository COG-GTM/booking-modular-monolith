using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
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

public class UpdateFlightRequestTests : FlightEndToEndTestBase
{
    public UpdateFlightRequestTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("passenger-api", HttpStatusCode.Forbidden)]
    public async Task requires_authentication_and_flight_scope(string? scope, HttpStatusCode expectedStatus)
    {
        using var client = Fixture.HttpClient;
        if (scope is null)
        {
            client.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            client.SetFakeBearerToken(
                new Dictionary<string, object> { { ClaimTypes.Role, IdentityConstant.Role.Admin }, { "scope", scope } }
            );
        }

        using var response = await client.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, await CreateRequestAsync());

        response.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task rejects_zero_price_without_updating_the_flight()
    {
        var request = await CreateRequestAsync();
        using var client = Fixture.HttpClient;
        using var response = await client.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, request with { Price = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var flight = await Fixture.FindAsync<FlightEntity, FlightId>(FlightId.Of(request.Id));
        flight.Price.Value.Should().Be(request.Price);
    }

    [Fact]
    public async Task ignores_the_legacy_is_deleted_field_while_updating_the_flight()
    {
        var request = await CreateRequestAsync();
        var newPrice = request.Price + 100;
        var payload = JsonSerializer
            .SerializeToNode(request with { Price = newPrice }, JsonSerializerOptions.Web)!
            .AsObject();
        payload["isDeleted"] = true;

        using var client = Fixture.HttpClient;
        using var response = await client.PutAsJsonAsync(ApiRoutes.Flight.UpdateFlight, payload);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var flight = await Fixture.FindAsync<FlightEntity, FlightId>(FlightId.Of(request.Id));
        flight.Should().NotBeNull();
        flight.IsDeleted.Should().BeFalse();
        flight.Price.Value.Should().Be(newPrice);
    }

    private async Task<UpdateFlightRequestDto> CreateRequestAsync()
    {
        var flight = await Fixture.FindAsync<FlightEntity, FlightId>(InitialData.Flights.First().Id);

        return new UpdateFlightRequestDto(
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
    }
}
