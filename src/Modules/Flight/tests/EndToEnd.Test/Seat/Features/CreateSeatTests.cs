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

namespace EndToEnd.Test.Seat.Features;

public class CreateSeatTests : FlightEndToEndTestBase
{
    public CreateSeatTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_create_seat_when_user_is_admin()
    {
        //Arrange
        var command = new FakeCreateSeatCommand().Generate();

        // Act
        var route = ApiRoutes.Seat.CreateSeat;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_return_forbidden_when_user_is_not_admin()
    {
        //Arrange
        var command = new FakeCreateSeatCommand().Generate();
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Seat.CreateSeat;
        var result = await httpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
