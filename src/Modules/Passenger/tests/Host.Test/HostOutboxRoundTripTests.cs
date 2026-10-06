namespace Host.Test;

using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Passenger.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using PassengerRoot = global::Passenger.PassengerRoot;

public sealed class HostOutboxRoundTripTests : PassengerHostTestBase
{
    public HostOutboxRoundTripTests(
        BuildingBlocks.TestBase.TestFixture<
            global::Passenger.Host.Program,
            PassengerDbContext,
            PassengerReadDbContext
        > fixture
    )
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
            .Be(typeof(IPersistMessageDbContext<PassengerRoot>));

        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IBus>().Address.Scheme.Should().Be("rabbitmq");
        var outbox = services.GetRequiredService<IPersistMessageDbContext<PassengerRoot>>();
        var actualDatabase = ((DbContext)outbox).Database.GetDbConnection().Database;
        var configuredDatabase = new NpgsqlConnectionStringBuilder(
            Fixture.Configuration["PostgresOptions:ConnectionString:Passenger"]
        ).Database;
        actualDatabase.Should().Be(configuredDatabase);

        var message = new PassengerCreated(Guid.NewGuid());
        await services.GetRequiredService<IEventDispatcher<PassengerRoot>>().SendAsync(message);

        (await outbox.PersistMessage.AnyAsync(item => item.DataType == typeof(PassengerCreated).ToString()))
            .Should()
            .BeTrue();
        (await Fixture.WaitForPublishing<PassengerCreated>()).Should().BeTrue();
    }
}
