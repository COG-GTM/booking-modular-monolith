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

public class CreateFlightTests : FlightEndToEndTestBase
{
    public CreateFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_create_new_flight_to_db_and_publish_message_to_broker()
    {
        //Arrange
        var command = new FakeCreateFlightCommand().Generate();

        // Act
        var route = ApiRoutes.Flight.CreateFlight;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task should_forbid_user_from_creating_a_flight()
    {
        var command = new FakeCreateFlightCommand().Generate();
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        var result = await client.PostAsJsonAsync(ApiRoutes.Flight.CreateFlight, command);

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
