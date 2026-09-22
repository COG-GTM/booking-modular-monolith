using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test;

public class MonolithHostTests : PassengerIntegrationTestBase
{
    public MonolithHostTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public void should_not_reference_flight_module()
    {
        // Act
        var referencedAssemblies = typeof(Program).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToList();

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name).ToList();

        // Assert
        referencedAssemblies.Should().Contain(["Identity", "Passenger", "Booking"]);
        referencedAssemblies.Should().NotContain(["Flight", "Flight.Api"]);
        loadedAssemblies.Should().NotContain(["Flight", "Flight.Api"]);
    }

    [Fact]
    public void should_register_composite_event_mapper_for_remaining_modules()
    {
        // Arrange
        using var scope = Fixture.ServiceProvider.CreateScope();

        // Act
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        // Assert
        eventMapper.Should().BeOfType<CompositeEventMapper>();
        scope.ServiceProvider.GetRequiredService<PassengerEventMapper>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_masstransit_bus()
    {
        // Act
        var bus = Fixture.ServiceProvider.GetService<IBus>();

        // Assert
        bus.Should().NotBeNull();
    }

    [Fact]
    public async Task should_return_monolith_name_from_root_endpoint()
    {
        // Arrange
        var appOptions = Fixture.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        // Act
        var result = await Fixture.HttpClient.GetAsync("/");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await result.Content.ReadAsStringAsync()).Should().Be(appOptions.Name);
    }

    [Fact]
    public async Task should_not_serve_flight_endpoints()
    {
        // Act
        var result = await Fixture.HttpClient.GetAsync("api/v1.0/flight/get-available-flights");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
