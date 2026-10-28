using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flight.Host.Test.Composition;

public sealed class FlightHostCompositionTests(
    TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext> fixture
) : FlightHostTestBase(fixture)
{
    [Fact]
    public void service_provider_contains_only_flight_module_persistence_and_events()
    {
        Assert.NotNull(Fixture.ServiceProvider.GetRequiredService<IPersistMessageProcessor<FlightRoot>>());

        Assert.Null(Fixture.ServiceProvider.GetService<IPersistMessageProcessor<OtherModule>>());
        Assert.Null(Fixture.ServiceProvider.GetService<PersistMessageDbContext<OtherModule>>());

        var persistenceRegistrations = Fixture.ServiceProvider.GetServices<ModulePersistenceRegistration>().ToArray();
        var persistenceRegistration = Assert.Single(persistenceRegistrations);
        Assert.Equal(typeof(FlightRoot), persistenceRegistration.ModuleType);

        var eventMappers = Fixture.ServiceProvider.GetServices<IEventMapper>().ToArray();
        var flightMapper = Assert.Single(eventMappers);
        Assert.IsType<FlightEventMapper>(flightMapper);
    }

    private sealed class OtherModule { }
}
