using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Host;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using FlightRoot = global::Flight.FlightRoot;

namespace Integration.Test;

public sealed class FlightOutboxRoundTripTests : FlightIntegrationTestBase
{
    public FlightOutboxRoundTripTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    protected override void RegisterTestsServices(IServiceCollection services)
    {
        base.RegisterTestsServices(services);
        services.Configure<PersistMessageOptions>(options => options.Interval = 1);
    }

    [Fact]
    public async Task host_dispatches_and_publishes_from_its_own_rabbitmq_outbox()
    {
        Fixture
            .PersistMessageDbContextTypes.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(IPersistMessageDbContext<FlightRoot>));

        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IBus>().Address.Scheme.Should().Be("rabbitmq");
        var outbox = services.GetRequiredService<IPersistMessageDbContext<FlightRoot>>();
        var actualDatabase = ((DbContext)outbox).Database.GetDbConnection().Database;
        var configuredDatabase = new NpgsqlConnectionStringBuilder(
            Fixture.Configuration["PostgresOptions:ConnectionString:Flight"]
        ).Database;
        actualDatabase.Should().Be(configuredDatabase);

        var message = new FlightCreated(Guid.NewGuid());
        await services.GetRequiredService<IEventDispatcher<FlightRoot>>().SendAsync(message);

        (await outbox.PersistMessage.AnyAsync(item => item.DataType == typeof(FlightCreated).ToString()))
            .Should()
            .BeTrue();
        (await Fixture.WaitForPublishing<FlightCreated>()).Should().BeTrue();
    }
}
