using System.Threading.Tasks;
using Api;
using Booking;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Identity;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Xunit;

namespace Integration.Test;

public class SharedInfrastructureTests : FlightIntegrationTestBase
{
    public SharedInfrastructureTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_serve_root_endpoint_with_app_name()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Booking-Modular-Monolith");
    }

    [Fact]
    public void should_register_composite_event_mapper_with_all_module_mappers()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        eventMapper.Should().BeOfType<CompositeEventMapper>();

        scope.ServiceProvider.GetService<global::Flight.FlightEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IdentityEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetService<PassengerEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetService<BookingEventMapper>().Should().NotBeNull();
    }
}
