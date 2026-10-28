using System.Net;
using System.Text.Json;
using BuildingBlocks.TestBase;
using Flight.Data;
using Xunit;

namespace Flight.Host.Test.Health;

public sealed class HealthEndpointTests(
    TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext> fixture
) : FlightHostTestBase(fixture)
{
    [Fact]
    public async Task live_and_ready_endpoints_report_healthy()
    {
        var liveResponse = await Fixture.HttpClient.GetAsync("/health/live");
        var readyResponse = await Fixture.HttpClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        using var ready = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            new[] { "self", "postgres-flight", "postgres-flight-persist-message", "mongo-flight" }.Order().ToArray(),
            ready.RootElement
                .GetProperty("checks")
                .EnumerateArray()
                .Select(check => check.GetProperty("name").GetString())
                .Order()
                .ToArray()
        );
    }
}
