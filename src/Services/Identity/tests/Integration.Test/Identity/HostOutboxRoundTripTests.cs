namespace Identity.Host.Integration.Test.Identity;

using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using IdentityContext = global::Identity.Data.IdentityContext;
using IdentityHostIntegrationTestBase = global::Identity.Host.Integration.Test.IdentityHostIntegrationTestBase;
using IdentityHostProgram = global::Identity.Host.Program;
using IdentityRoot = global::Identity.IdentityRoot;

public sealed class HostOutboxRoundTripTests : IdentityHostIntegrationTestBase
{
    public HostOutboxRoundTripTests(TestWriteFixture<IdentityHostProgram, IdentityContext> fixture)
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
            .Be(typeof(IPersistMessageDbContext<IdentityRoot>));

        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IBus>().Address.Scheme.Should().Be("rabbitmq");
        var outbox = services.GetRequiredService<IPersistMessageDbContext<IdentityRoot>>();
        var actualDatabase = ((DbContext)outbox).Database.GetDbConnection().Database;
        var configuredDatabase = new NpgsqlConnectionStringBuilder(
            Fixture.Configuration["PostgresOptions:ConnectionString:Identity"]
        ).Database;
        actualDatabase.Should().Be(configuredDatabase);

        var message = new UserCreated(Guid.NewGuid(), "AB-247 Identity", Guid.NewGuid().ToString("N"));
        await services.GetRequiredService<IEventDispatcher<IdentityRoot>>().SendAsync(message);

        (await outbox.PersistMessage.AnyAsync(item => item.DataType == typeof(UserCreated).ToString()))
            .Should()
            .BeTrue();
        (await Fixture.WaitForPublishing<UserCreated>()).Should().BeTrue();
    }
}
