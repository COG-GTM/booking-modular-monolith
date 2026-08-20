using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlightService.Integration.Test;

public class HostSmokeTests : FlightServiceIntegrationTestBase
{
    public HostSmokeTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task root_endpoint_should_return_app_name()
    {
        var result = await Fixture.HttpClient.GetAsync("/");

        result.IsSuccessStatusCode.Should().BeTrue();

        var content = await result.Content.ReadAsStringAsync();
        content.Should().Be("Flight-Service");
    }

    [Fact]
    public void flight_module_services_should_be_resolvable()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMediator>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FlightDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FlightReadDbContext>().Should().NotBeNull();
    }
}
