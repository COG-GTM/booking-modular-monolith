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

namespace EndToEnd.Test.Aircraft.Features;

public class CreateAircraftTests : FlightEndToEndTestBase
{
    public CreateAircraftTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_create_aircraft_when_user_is_admin()
    {
        //Arrange
        var command = new FakeCreateAircraftCommand().Generate();

        // Act
        var route = ApiRoutes.Aircraft.CreateAircraft;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_return_forbidden_when_user_is_not_admin()
    {
        //Arrange
        var command = new FakeCreateAircraftCommand().Generate();
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Aircraft.CreateAircraft;
        var result = await httpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
