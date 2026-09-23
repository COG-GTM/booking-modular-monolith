using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EndToEnd.Test.Airport.Features;

using global::Flight.Airports.Features.CreatingAirport.V1;
using global::Flight.Airports.ValueObjects;

public class CreateAirportTests : FlightEndToEndTestBase
{
    public CreateAirportTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_create_new_airport_and_return_ok_with_created_id()
    {
        // Arrange
        var command = new FakeCreateAirportCommand().Generate();

        // Act
        var route = ApiRoutes.Airport.CreateAirport;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CreateAirportResponseDto>();
        response.Should().NotBeNull();
        response!.Id.Should().NotBeEmpty();

        var airport = await Fixture.ExecuteDbContextAsync(db =>
            db.Airports.SingleOrDefaultAsync(x => x.Id == AirportId.Of(response.Id))
        );

        airport.Should().NotBeNull();
        airport!.Code.Value.Should().Be(command.Code);
        airport.Name.Value.Should().Be(command.Name);
        airport.Address.Value.Should().Be(command.Address);
    }

    [Fact]
    public async Task should_return_unauthorized_when_request_has_no_bearer_token()
    {
        // Arrange
        var command = new FakeCreateAirportCommand().Generate();
        var httpClient = Fixture.HttpClient;
        httpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        var route = ApiRoutes.Airport.CreateAirport;
        var result = await httpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
