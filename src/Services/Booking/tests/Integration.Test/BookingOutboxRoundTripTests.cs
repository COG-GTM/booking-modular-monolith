namespace Booking.Host.Integration.Test;

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
using BookingHostProgram = global::Booking.Host.Program;
using BookingRoot = global::Booking.BookingRoot;

[Collection(BookingHostIntegrationTestCollection.Name)]
public sealed class BookingOutboxRoundTripTests : TestFixtureCore<BookingHostProgram>
{
    public BookingOutboxRoundTripTests(BookingHostFixture fixture)
        : base(fixture, outputHelper: null) { }

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
            .Be(typeof(IPersistMessageDbContext<BookingRoot>));

        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IBus>().Address.Scheme.Should().Be("rabbitmq");
        var outbox = services.GetRequiredService<IPersistMessageDbContext<BookingRoot>>();
        var actualDatabase = ((DbContext)outbox).Database.GetDbConnection().Database;
        var configuredDatabase = new NpgsqlConnectionStringBuilder(
            Fixture.Configuration["PostgresOptions:ConnectionString:Booking"]
        ).Database;
        actualDatabase.Should().Be(configuredDatabase);

        var message = new BookingCreated(Guid.NewGuid());
        await services.GetRequiredService<IEventDispatcher<BookingRoot>>().SendAsync(message);

        (await outbox.PersistMessage.AnyAsync(item => item.DataType == typeof(BookingCreated).ToString()))
            .Should()
            .BeTrue();
        (await Fixture.WaitForPublishing<BookingCreated>()).Should().BeTrue();
    }
}

public sealed class BookingHostFixture : TestFixture<BookingHostProgram> { }

[CollectionDefinition(Name)]
public sealed class BookingHostIntegrationTestCollection : ICollectionFixture<BookingHostFixture>
{
    public const string Name = "Booking Host Integration";
}
