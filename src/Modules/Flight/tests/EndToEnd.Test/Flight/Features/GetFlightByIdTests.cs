using System.Net;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Flights.Exceptions;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class GetFlightByIdTests : FlightEndToEndTestBase
{
    public GetFlightByIdTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_retrive_a_flight_by_id_currectly()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var route = ApiRoutes.Flight.GetFlightById.Replace(
            ApiRoutes.Flight.Id,
            command.Id.ToString(),
            StringComparison.CurrentCulture
        );
        var result = await Fixture.HttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_return_error_status_for_unknown_flight_id()
    {
        // Act
        var route = ApiRoutes.Flight.GetFlightById.Replace(
            ApiRoutes.Flight.Id,
            Guid.NewGuid().ToString(),
            StringComparison.CurrentCulture
        );
        var result = await Fixture.HttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await result.Content.ReadAsStringAsync()).Should().Contain(nameof(FlightNotFountException));
    }
}
