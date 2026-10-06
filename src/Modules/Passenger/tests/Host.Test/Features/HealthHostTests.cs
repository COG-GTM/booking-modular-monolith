using System.Net;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Passenger.Data;
using Xunit;

namespace Host.Test.Features;

public class HealthHostTests : PassengerHostTestBase
{
    public HealthHostTests(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_report_healthy_on_health_endpoint()
    {
        // Act
        var response = await Fixture.HttpClient.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
