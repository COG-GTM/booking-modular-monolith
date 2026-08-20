using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.TestBase;
using Flight.Data;
using FlightService.Integration.Test.Fakes;
using FlightService.Integration.Test.Routes;
using FluentAssertions;
using Xunit;

namespace FlightService.Integration.Test.Flight.Features;

public class CreateFlightTests : FlightServiceIntegrationTestBase
{
    public CreateFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_create_new_flight_through_standalone_host()
    {
        // Arrange
        var command = new FakeCreateFlightCommand().Generate();

        // Act
        var route = ApiRoutes.Flight.CreateFlight;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
