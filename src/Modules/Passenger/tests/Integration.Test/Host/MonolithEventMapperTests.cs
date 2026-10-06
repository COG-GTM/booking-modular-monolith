using Api;
using Booking;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using Flight;
using FluentAssertions;
using Identity;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Host;

// The monolith registers one IEventMapper per module; the event dispatcher composes those mappers internally.
public class MonolithEventMapperTests : PassengerIntegrationTestBase
{
    public MonolithEventMapperTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public void should_register_one_event_mapper_per_module_without_a_composite()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var mappers = scope.ServiceProvider.GetServices<IEventMapper>().ToList();

        mappers.Should().HaveCount(4);
        mappers.Should().ContainSingle(m => m is FlightEventMapper);
        mappers.Should().ContainSingle(m => m is IdentityEventMapper);
        mappers.Should().ContainSingle(m => m is PassengerEventMapper);
        mappers.Should().ContainSingle(m => m is BookingEventMapper);
        mappers.Should().NotContain(m => m is CompositeEventMapper);

        scope.ServiceProvider.GetRequiredService<FlightEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IdentityEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PassengerEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<BookingEventMapper>().Should().NotBeNull();
    }

    [Fact]
    public void should_resolve_the_module_event_dispatcher()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventDispatcher<PassengerRoot>>().Should().NotBeNull();
    }
}
