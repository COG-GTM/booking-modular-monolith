using System.Net;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Passenger.Data;
using Xunit;

namespace Host.Test.Features;

// Uses the shared host fixture directly instead of PassengerHostTestBase: these requests touch no store or
// queue, and the base's per-test reset would drop the broker bindings that the consumer tests rely on.
[Collection(PassengerHostTestCollection.Name)]
public class RootEndpointHostTests(
    TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> fixture
)
{
    [Fact]
    public async Task root_should_return_the_passenger_service_name()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Passenger-Service");
    }

    [Fact]
    public async Task alive_should_report_healthy_on_liveness_endpoint()
    {
        // Act
        var response = await fixture.HttpClient.GetAsync("/alive");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
