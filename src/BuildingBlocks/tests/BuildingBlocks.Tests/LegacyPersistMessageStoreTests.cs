using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace BuildingBlocks.Tests;

public class LegacyPersistMessageStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task legacy_store_upgrades_in_place_and_can_enqueue_poll_and_complete_inbox(bool integerDefaults)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
        await postgres.StartAsync();
        var connection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Timezone = "America/New_York",
        }.ConnectionString;
        var expected = new List<PersistMessage>();
        DateTime created;
        await using (
            var legacy = new LegacyContext(
                new DbContextOptionsBuilder<LegacyContext>()
                    .UseNpgsql(connection)
                    .UseSnakeCaseNamingConvention()
                    .Options
            )
        )
        {
            await legacy.Database.EnsureCreatedAsync();
            if (integerDefaults)
                await legacy.Database.ExecuteSqlRawAsync(
                    $"""
                    ALTER TABLE persist_message ALTER COLUMN message_status SET DEFAULT {(int)MessageStatus.InProgress};
                    ALTER TABLE persist_message ALTER COLUMN delivery_type SET DEFAULT {(int)
                        MessageDeliveryType.Outbox};
                    """
                );
            foreach (var status in Enum.GetValues<MessageStatus>())
            foreach (var delivery in Enum.GetValues<MessageDeliveryType>())
            {
                var message = new UpgradeEvent(Guid.NewGuid());
                var row = new PersistMessage(
                    message.EventId,
                    typeof(UpgradeEvent).FullName!,
                    JsonSerializer.Serialize(new MessageEnvelope(message)),
                    delivery
                );
                row.ChangeState(status);
                expected.Add(row);
                legacy.PersistMessage.Add(row);
            }
            await legacy.SaveChangesAsync();
            created = await legacy
                .Database.SqlQuery<DateTime>(
                    $"""SELECT created AS "Value" FROM persist_message WHERE id = {expected[0].Id}"""
                )
                .SingleAsync();
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["ConnectionStrings:legacy-persist-message"] = connection;
        builder.AddPersistMessageProcessor<LegacyPersistMessageStoreTests>("Legacy");
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<
            IPersistMessageDbContext<LegacyPersistMessageStoreTests>
        >();
        var context = scope.ServiceProvider.GetRequiredService<
            PersistMessageDbContext<LegacyPersistMessageStoreTests>
        >();
        var upgraded = await store.PersistMessage.OrderBy(x => x.Id).ToListAsync();
        upgraded.Should().BeEquivalentTo(expected, options => options.Excluding(x => x.Created));
        var storedCreated = await context
            .Database.SqlQuery<DateTime>(
                $"""SELECT created AS "Value" FROM persist_message WHERE id = {expected[0].Id}"""
            )
            .SingleAsync();
        storedCreated.Should().Be(created);
        var columns = await context
            .Database.SqlQueryRaw<string>(
                """
                SELECT data_type AS "Value" FROM information_schema.columns
                WHERE table_name = 'persist_message' AND column_name IN ('message_status', 'delivery_type', 'created')
                """
            )
            .ToListAsync();
        columns.Should().BeEquivalentTo(["text", "text", "timestamp without time zone"]);
        var defaults = await context
            .Database.SqlQueryRaw<string>(
                """
                SELECT column_default AS "Value" FROM information_schema.columns
                WHERE table_name = 'persist_message' AND column_name IN ('message_status', 'delivery_type')
                """
            )
            .ToListAsync();
        defaults.Should().BeEquivalentTo(["'InProgress'::text", "'Outbox'::text"]);

        await using (var secondScope = provider.CreateAsyncScope())
        {
            var second = secondScope.ServiceProvider.GetRequiredService<
                IPersistMessageDbContext<LegacyPersistMessageStoreTests>
            >();
            (await second.PersistMessage.CountAsync()).Should().Be(expected.Count);
        }

        var publisher = Substitute.For<IPublishEndpoint>();
        var processor = new PersistMessageProcessor<LegacyPersistMessageStoreTests>(
            NullLogger<PersistMessageProcessor<LegacyPersistMessageStoreTests>>.Instance,
            Substitute.For<IMediator>(),
            store,
            publisher
        );
        await processor.PublishMessageAsync(new MessageEnvelope(new UpgradeEvent(Guid.NewGuid())));
        var inbox = await processor.AddReceivedMessageAsync(new MessageEnvelope(new UpgradeEvent(Guid.NewGuid())));
        await processor.ProcessInboxAsync(inbox);
        await processor.ProcessAllAsync();
        (await processor.GetInboxMessageAsync(inbox))!.MessageStatus.Should().Be(MessageStatus.Processed);
        (await store.PersistMessage.Where(x => x.DeliveryType == MessageDeliveryType.Outbox).ToListAsync())
            .Should()
            .OnlyContain(x => x.MessageStatus == MessageStatus.Processed);
        publisher.ReceivedCalls().Should().HaveCount(3);
        context.ChangeTracker.Clear();
        (await store.PersistMessage.CountAsync()).Should().Be(expected.Count + 2);
    }

    public sealed record UpgradeEvent(Guid EventId) : IIntegrationEvent;

    private sealed class LegacyContext(DbContextOptions<LegacyContext> options) : DbContext(options)
    {
        public DbSet<PersistMessage> PersistMessage => Set<PersistMessage>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PersistMessage>().Property(x => x.Created).HasColumnType("timestamp without time zone");
            builder.ToSnakeCaseTables();
        }
    }
}
