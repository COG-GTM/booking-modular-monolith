using System.Net;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Api;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test;

public class FlightApiHostTests : FlightEndToEndTestBase
{
    public FlightApiHostTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_return_flight_service_name_from_root_endpoint()
    {
        // Act
        var result = await Fixture.HttpClient.GetAsync("/");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await result.Content.ReadAsStringAsync()).Should().Be("Flight-Service");
    }

    [Fact]
    public async Task should_serve_flight_endpoints_from_standalone_host()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var result = await Fixture.HttpClient.GetAsync(ApiRoutes.Flight.GetAvailableFlights);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await result.Content.ReadAsStringAsync()).Should().Contain(command.Id.ToString());
    }
}
