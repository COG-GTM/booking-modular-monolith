using System.Net;
using BuildingBlocks.TestBase;
using Flight.Data;
using FlightService.Integration.Test.Fakes;
using FlightService.Integration.Test.Routes;
using FluentAssertions;
using Xunit;

namespace FlightService.Integration.Test.Flight.Features;

public class GetFlightByIdTests : FlightServiceIntegrationTestBase
{
    public GetFlightByIdTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_retrieve_a_flight_by_id_through_standalone_host()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var route = ApiRoutes.Flight.GetFlightById.Replace(ApiRoutes.Flight.Id, command.Id.ToString(), StringComparison.CurrentCulture);
        var result = await Fixture.HttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
