using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

// PersistMessageProcessor<TModule> persists into PersistMessageDbContext<TModule> only, and the Aspire
// connection-string key is derived from the kebab-cased module name (AB-492).
public class PerModulePersistMessageStoreTests
{
    public sealed class FlightModule;

    public sealed class PassengerModule;

    public sealed record FakeIntegrationEvent(Guid Id, string Name) : IIntegrationEvent
    {
        public Guid EventId => Id;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void connection_string_aspire_key_is_kebab_cased_from_module_name()
    {
        var configuration = Configuration(
            new() { ["ConnectionStrings:flight-schedule-persist-message"] = "Server=aspire;Database=fs" }
        );

        Assert.Equal(
            "Server=aspire;Database=fs",
            Extensions.GetPersistMessageConnectionString(configuration, "FlightSchedule")
        );
    }

    [Fact]
    public async Task processor_writes_only_to_its_own_modules_store()
    {
        await using var flightStore = CreateStore<FlightModule>();
        await using var passengerStore = CreateStore<PassengerModule>();

        var flightProcessor = new PersistMessageProcessor<FlightModule>(
            NullLogger<PersistMessageProcessor<FlightModule>>.Instance,
            Substitute.For<IMediator>(),
            flightStore,
            Substitute.For<IPublishEndpoint>()
        );

        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "flight-created");
        await flightProcessor.PublishMessageAsync(new MessageEnvelope(@event));

        var persisted = Assert.Single(await flightStore.PersistMessage.ToListAsync());
        Assert.Equal(@event.Id, persisted.Id);
        Assert.Equal(MessageDeliveryType.Outbox, persisted.DeliveryType);
        Assert.Equal(MessageStatus.InProgress, persisted.MessageStatus);
        Assert.Equal(typeof(FakeIntegrationEvent).ToString(), persisted.DataType);
        Assert.Empty(await passengerStore.PersistMessage.ToListAsync());
    }

    private static PersistMessageDbContext<TModule> CreateStore<TModule>()
        where TModule : class
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TModule>>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PersistMessageDbContext<TModule>(options);
    }
}
