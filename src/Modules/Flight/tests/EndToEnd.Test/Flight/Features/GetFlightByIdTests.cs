using System.Net;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class GetFlightByIdTests : FlightEndToEndTestBase
{
    public GetFlightByIdTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }


    [Fact]
    public async Task should_retrive_a_flight_by_id_currectly()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var route = ApiRoutes.Flight.GetFlightById.Replace(ApiRoutes.Flight.Id, command.Id.ToString(), StringComparison.CurrentCulture);
        var result = await Fixture.HttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_retrieve_a_flight_by_id_when_user_is_not_admin()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Flight.GetFlightById.Replace(ApiRoutes.Flight.Id, command.Id.ToString(), StringComparison.CurrentCulture);
        var result = await httpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}